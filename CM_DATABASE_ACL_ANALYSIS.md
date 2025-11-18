# Content Manager Database ACL Structure Analysis

**Database:** CM_DB1
**Server:** OTX-1Y0GDY3
**Date:** 2025-11-17

---

## 📊 Executive Summary

- **Total Records:** 265
- **Records with ACL:** 1 (0.4%)
- **Unrestricted Records:** 264 (99.6%)
- **Total Users:** 4 (UKHAN2, Pradeepa, CMUser, + system users)
- **Total Groups:** 1 (GroupUK)

---

## 🗄️ Content Manager ACL Database Structure

### **Core ACL Tables**

#### **1. TSACLGROUP** - ACL Group Definitions
Defines ACL groups that can be applied to records.

```sql
Table: TSACLGROUP
├── uri (bigint, PK) - Unique identifier for ACL group
├── acgHash (nvarchar(64)) - Hash of ACL configuration
└── acgTimeStamp (char(15)) - Last modified timestamp
```

#### **2. TSACLGRPME** - ACL Group Members
Links ACL groups to users/locations (many-to-many).

```sql
Table: TSACLGRPME
├── agmAclGroup (bigint, FK -> TSACLGROUP.uri) - ACL Group ID
└── agmLocation (bigint, FK -> TSLOCATION.uri) - User/Location ID
```

**Example Data:**
```
ACL Group 1 -> Location 1 (UKHAN2)
```

#### **3. TSLOCATION** - Users and Locations
Contains all users, groups, and organizational locations.

```sql
Table: TSLOCATION
├── uri (bigint, PK) - Unique identifier
├── lcName (nvarchar(400)) - User/Location name
├── lcType (smallint) - Type: 0=System, 2=Group, 4=User
├── lcNickname (nchar(20)) - Short name
├── lcPhone, lcMobile, lcFaxNumber - Contact info
├── lcDirSyncDN (nvarchar(256)) - Active Directory DN
├── lcDirSyncEnabled (char(1)) - AD sync enabled
└── ... (26 columns total)
```

**Location Types:**
- `lcType = 0`: System/Service accounts
- `lcType = 2`: Groups
- `lcType = 4`: Individual users

**Sample Data:**
```
uri | lcName      | lcType
----|-------------|-------
1   | UKHAN2      | 4 (User)
2   | TRIMServices| 0 (System)
3   | Pradeepa    | 4 (User)
4   | CMUser      | 4 (User)
5   | GroupUK     | 2 (Group)
```

#### **4. TSRECORD** - Main Record Table
Contains all Content Manager records with ACL references.

```sql
Table: TSRECORD (Key ACL Columns)
├── uri (bigint, PK) - Record URI
├── title (nvarchar) - Record title
├── rcAclGroupKey (bigint, FK -> TSACLGROUP.uri) - ACL Group for this record
├── rcAclContainer (bigint) - Inherited ACL from container
├── rcAclExclusion (bigint) - ACL exclusions
├── rcOrContainerAclUri (bigint) - Original container ACL
├── rcSecLevel (smallint) - Security level
└── rcRecTypeSecFilter (bigint) - Record type security filter
```

**Key Field:** `rcAclGroupKey`
- **NULL:** Record is unrestricted (accessible to all)
- **NOT NULL:** Record has ACL restrictions (links to TSACLGROUP)

**Current Data:**
```
uri | title               | rcAclGroupKey | Access
----|---------------------|---------------|--------
2   | CM9.4_ServiceAPI    | 1             | UKHAN2 only
1   | Rec-001             | NULL          | <Unrestricted>
3   | 1-5500-234-12       | NULL          | <Unrestricted>
... | ...                 | NULL          | <Unrestricted>
```

---

## 🔄 ACL Relationships

```
┌──────────────┐         ┌──────────────┐         ┌──────────────┐
│  TSRECORD    │         │  TSACLGROUP  │         │ TSLOCATION   │
│              │         │              │         │              │
│ uri: 2       │────────▶│ uri: 1       │◀────────│ uri: 1       │
│ title: ...   │  FK     │ (ACL Def)    │  M:M    │ lcName:      │
│ rcAclGroupKey│         │              │ via     │ UKHAN2       │
│   = 1        │         │              │ GRPME   │ lcType: 4    │
└──────────────┘         └──────────────┘         └──────────────┘
                                                          │
                                                          │
                                             ┌────────────┴─────────────┐
                                             │                          │
                                       ┌──────────────┐         ┌──────────────┐
                                       │ TSACLGRPME   │         │ TSLOCATION   │
                                       │              │         │              │
                                       │agmAclGroup: 1│         │ uri: 5       │
                                       │agmLocation: 1│         │ lcName:      │
                                       │              │         │ GroupUK      │
                                       └──────────────┘         │ lcType: 2    │
                                                                └──────────────┘
                                                                  (Group)
```

---

## 🔍 ACL Resolution Logic

### **When User Requests Record:**

```sql
-- Check if user has access to record
SELECT r.uri, r.title
FROM TSRECORD r
WHERE r.uri = @RecordUri
  AND (
    -- Record is unrestricted
    r.rcAclGroupKey IS NULL
    OR
    -- User is in the ACL group
    EXISTS (
      SELECT 1
      FROM TSACLGRPME gm
      INNER JOIN TSLOCATION loc ON gm.agmLocation = loc.uri
      WHERE gm.agmAclGroup = r.rcAclGroupKey
        AND loc.lcName = @CurrentUsername  -- e.g., 'UKHAN2'
    )
    OR
    -- User belongs to a group in the ACL group
    EXISTS (
      SELECT 1
      FROM TSACLGRPME gm
      INNER JOIN TSLOCATION grp ON gm.agmLocation = grp.uri
      INNER JOIN TSLOCATION usr ON usr.lcName = @CurrentUsername
      WHERE gm.agmAclGroup = r.rcAclGroupKey
        AND grp.lcType = 2  -- Group
        AND grp.uri IN (/* user's groups */)
    )
  )
```

---

## 📋 PostgreSQL Sync Schema Design

Based on the Content Manager structure, here's the optimal PostgreSQL schema:

```sql
-- ============================================================
-- POSTGRESQL ACL SYNC SCHEMA
-- Mirrors Content Manager ACL structure for vector search
-- ============================================================

-- Table 1: ACL Groups (mirrors TSACLGROUP)
CREATE TABLE IF NOT EXISTS cm_acl_groups (
    acl_group_id BIGINT PRIMARY KEY,  -- From CM TSACLGROUP.uri
    acl_hash VARCHAR(64),
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_cm_acl_groups_hash ON cm_acl_groups(acl_hash);

-- Table 2: Users/Locations (mirrors TSLOCATION)
CREATE TABLE IF NOT EXISTS cm_locations (
    location_id BIGINT PRIMARY KEY,  -- From CM TSLOCATION.uri
    location_name VARCHAR(400) NOT NULL,  -- From CM lcName
    location_type SMALLINT NOT NULL,  -- From CM lcType (0=System, 2=Group, 4=User)
    nickname VARCHAR(20),
    ad_dn VARCHAR(256),  -- Active Directory DN
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_cm_locations_name ON cm_locations(location_name);
CREATE INDEX idx_cm_locations_type ON cm_locations(location_type);

-- Table 3: ACL Group Members (mirrors TSACLGRPME)
CREATE TABLE IF NOT EXISTS cm_acl_group_members (
    id SERIAL PRIMARY KEY,
    acl_group_id BIGINT NOT NULL REFERENCES cm_acl_groups(acl_group_id),
    location_id BIGINT NOT NULL REFERENCES cm_locations(location_id),
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW(),

    UNIQUE(acl_group_id, location_id)
);

CREATE INDEX idx_cm_acl_members_group ON cm_acl_group_members(acl_group_id);
CREATE INDEX idx_cm_acl_members_location ON cm_acl_group_members(location_id);

-- Table 4: Record ACL Mapping (from TSRECORD)
CREATE TABLE IF NOT EXISTS cm_record_acl (
    record_uri BIGINT PRIMARY KEY,
    acl_group_id BIGINT REFERENCES cm_acl_groups(acl_group_id),
    is_unrestricted BOOLEAN GENERATED ALWAYS AS (acl_group_id IS NULL) STORED,
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_cm_record_acl_group ON cm_record_acl(acl_group_id);
CREATE INDEX idx_cm_record_acl_unrestricted ON cm_record_acl(is_unrestricted);

-- Table 5: User Group Memberships (cached for performance)
CREATE TABLE IF NOT EXISTS cm_user_groups (
    user_location_id BIGINT NOT NULL REFERENCES cm_locations(location_id),
    group_location_id BIGINT NOT NULL REFERENCES cm_locations(location_id),
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW(),

    PRIMARY KEY(user_location_id, group_location_id)
);

CREATE INDEX idx_cm_user_groups_user ON cm_user_groups(user_location_id);
CREATE INDEX idx_cm_user_groups_group ON cm_user_groups(group_location_id);

-- ============================================================
-- SYNC STATUS TRACKING
-- ============================================================

CREATE TABLE IF NOT EXISTS cm_acl_sync_log (
    id SERIAL PRIMARY KEY,
    sync_type VARCHAR(50) NOT NULL,  -- 'full', 'incremental', 'acl_only'
    records_synced INT NOT NULL DEFAULT 0,
    acl_groups_synced INT NOT NULL DEFAULT 0,
    locations_synced INT NOT NULL DEFAULT 0,
    started_at TIMESTAMP NOT NULL,
    completed_at TIMESTAMP,
    status VARCHAR(20) NOT NULL,  -- 'running', 'completed', 'failed'
    error_message TEXT
);

CREATE INDEX idx_cm_sync_log_status ON cm_acl_sync_log(status);
CREATE INDEX idx_cm_sync_log_started ON cm_acl_sync_log(started_at);
```

---

## 🎯 Vector Search ACL Filtering Query

### **Query to Get Accessible Records for Current User:**

```sql
-- Get accessible record URIs for user "UKHAN2"
WITH user_info AS (
    SELECT location_id, location_name
    FROM cm_locations
    WHERE location_name = 'UKHAN2' AND location_type = 4
),
user_groups AS (
    -- Get all groups this user belongs to
    SELECT ug.group_location_id
    FROM cm_user_groups ug
    INNER JOIN user_info ui ON ug.user_location_id = ui.location_id
)
SELECT DISTINCT e.record_uri, e.record_title, e.vector
FROM embeddings e
INNER JOIN cm_record_acl ra ON e.record_uri = ra.record_uri
WHERE (
    -- Unrestricted records
    ra.is_unrestricted = TRUE
    OR
    -- User has direct access
    EXISTS (
        SELECT 1
        FROM cm_acl_group_members gm
        INNER JOIN user_info ui ON gm.location_id = ui.location_id
        WHERE gm.acl_group_id = ra.acl_group_id
    )
    OR
    -- User belongs to a group with access
    EXISTS (
        SELECT 1
        FROM cm_acl_group_members gm
        INNER JOIN user_groups ug ON gm.location_id = ug.group_location_id
        WHERE gm.acl_group_id = ra.acl_group_id
    )
)
ORDER BY e.vector <=> :query_embedding
LIMIT 20;
```

---

## 🔧 Sync Strategy

### **Initial Full Sync:**

1. **Sync Locations** (Users & Groups)
   ```sql
   INSERT INTO cm_locations (location_id, location_name, location_type, ...)
   SELECT uri, lcName, lcType, ... FROM [CM_DB1].dbo.TSLOCATION
   ```

2. **Sync ACL Groups**
   ```sql
   INSERT INTO cm_acl_groups (acl_group_id, acl_hash, ...)
   SELECT uri, acgHash, ... FROM [CM_DB1].dbo.TSACLGROUP
   ```

3. **Sync ACL Group Members**
   ```sql
   INSERT INTO cm_acl_group_members (acl_group_id, location_id)
   SELECT agmAclGroup, agmLocation FROM [CM_DB1].dbo.TSACLGRPME
   ```

4. **Sync Record ACL Mappings**
   ```sql
   INSERT INTO cm_record_acl (record_uri, acl_group_id)
   SELECT uri, rcAclGroupKey FROM [CM_DB1].dbo.TSRECORD
   ```

### **Incremental Sync:**

Monitor Content Manager for ACL changes and update PostgreSQL accordingly:
- Check `sysLastUpdated` timestamps in TSLOCATION
- Check `acgTimeStamp` in TSACLGROUP
- Update PostgreSQL when changes detected

---

## 📈 Performance Considerations

### **Current Environment:**
- 265 total records
- Only 1 record with ACL restrictions (99.6% unrestricted)
- 4 users, 1 group

**Performance Impact:**
- **Minimal** - Most records are unrestricted
- ACL check is simple boolean (is_unrestricted = TRUE)
- Very few JOIN operations needed for 264/265 records

### **Recommendations:**

1. **For Current Scale (< 1000 records):**
   - ✅ **Solution 1 (Post-Search Validation)** is perfectly fine
   - Minimal overhead with only 1 restricted record

2. **For Future Growth (> 10,000 records with ACL):**
   - 🚀 **Solution 2 (PostgreSQL ACL Sync)** will provide better performance
   - Pre-filtering in database vs. post-validation in code

---

## 🎯 Recommendation

Given your **current environment**:
- 99.6% of records are unrestricted
- Only 4 users
- 1 ACL group

**Start with Solution 1 (Post-Search Validation):**
- Simple to implement
- No sync overhead
- Adequate performance for current scale

**Migrate to Solution 2 later if:**
- Number of ACL-restricted records grows significantly
- Number of users increases
- Performance becomes a concern

---

## 📝 Next Steps

1. ✅ **Implement Solution 1** - Post-Search ACL Validation
2. ⏳ **Monitor Performance** - Track ACL check overhead
3. 🔄 **Plan Migration** - Prepare Solution 2 if scale increases
4. 📊 **Track Growth** - Monitor ACL adoption rate

Would you like me to implement Solution 1 now?
