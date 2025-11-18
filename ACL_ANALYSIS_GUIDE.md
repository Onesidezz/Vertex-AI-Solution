# Content Manager ACL Analysis Guide

## Overview

This tool analyzes the ACL (Access Control List) structure from Content Manager using the Trim SDK. It extracts permission data to understand how ACL is organized and generates a PostgreSQL schema for syncing ACL data.

---

## 🎯 Purpose

Before implementing ACL filtering in your vector search, you need to:
1. **Understand** how Content Manager stores ACL information
2. **Identify** which users and groups have access to records
3. **Design** a PostgreSQL schema to store ACL data efficiently
4. **Plan** the sync strategy from Content Manager to PostgreSQL

This tool does all of that automatically!

---

## 📋 What the Tool Does

### 1. **Analyzes Content Manager ACL Structure**
   - Connects to Content Manager using Trim SDK (via Windows Auth)
   - Samples records to extract ACL information
   - Parses permission types: ViewDocument, UpdateDocument, etc.
   - Identifies users, groups, and locations with permissions

### 2. **Extracts Detailed Permission Data**
   - Raw ACL strings from Content Manager
   - Parsed users (e.g., "DOMAIN\user1", "john.doe")
   - Parsed groups (e.g., "Group:IT_Department", "Group:Managers")
   - Parsed locations (if applicable)

### 3. **Generates PostgreSQL Schema**
   - Creates tables for storing ACL data
   - Includes indexes for fast user/group lookups
   - Supports array-based filtering (PostgreSQL GIN indexes)
   - Includes sync tracking tables

---

## 🚀 How to Use

### **Step 1: Run the Analysis**

**Endpoint:**
```
GET /api/AclAnalysis/analyze?sampleSize=100
```

**Example using browser or Postman:**
```
https://localhost:5001/api/AclAnalysis/analyze?sampleSize=100
```

**Parameters:**
- `sampleSize` (optional): Number of records to analyze (default: 100)

**Response:**
```json
{
  "success": true,
  "summary": {
    "totalRecordsAnalyzed": 100,
    "recordsWithAcl": 95,
    "recordsWithoutAcl": 5,
    "uniqueUsersCount": 45,
    "uniqueGroupsCount": 12,
    "uniqueUsers": ["DOMAIN\\user1", "DOMAIN\\user2", ...],
    "uniqueGroups": ["IT_Department", "Managers", "Finance", ...],
    "permissionTypes": {
      "ViewDocument": 95,
      "ViewMetadata": 95,
      "UpdateDocument": 20,
      ...
    }
  },
  "samples": [
    {
      "recordUri": 12345,
      "recordTitle": "Sample Document",
      "rawAclString": "DOMAIN\\user1, Group:IT_Department",
      "permissions": [
        {
          "permissionName": "ViewDocument",
          "rawValue": "DOMAIN\\user1, Group:IT_Department",
          "users": ["DOMAIN\\user1"],
          "groups": ["IT_Department"],
          "locations": []
        }
      ]
    }
  ]
}
```

---

### **Step 2: Generate PostgreSQL Schema**

**Endpoint:**
```
GET /api/AclAnalysis/generate-schema?sampleSize=100
```

**Example:**
```
https://localhost:5001/api/AclAnalysis/generate-schema?sampleSize=100
```

**Response:**
```json
{
  "success": true,
  "schema": "CREATE TABLE IF NOT EXISTS record_acl (...);",
  "analysisSummary": {
    "totalRecordsAnalyzed": 100,
    "recordsWithAcl": 95,
    "uniqueUsersCount": 45,
    "uniqueGroupsCount": 12
  }
}
```

The schema will include:
- **`record_acl` table**: Stores which users/groups can access each record
- **`user_group_cache` table**: Caches Windows group memberships
- **`acl_sync_status` table**: Tracks sync progress

---

### **Step 3: Check ACL for Specific Record** (Optional)

**Endpoint:**
```
GET /api/AclAnalysis/record/{uri}
```

**Example:**
```
https://localhost:5001/api/AclAnalysis/record/12345
```

---

## 📊 Understanding the Results

### **Permission Types in Content Manager**

The tool analyzes 7 permission types:

| ID | Permission Name      | Description                           |
|----|---------------------|---------------------------------------|
| 1  | ViewDocument        | Can view document content             |
| 2  | ViewMetadata        | Can view record metadata              |
| 3  | UpdateDocument      | Can modify document content           |
| 4  | UpdateMetadata      | Can modify record metadata            |
| 5  | ModifyAccess        | Can change ACL permissions            |
| 6  | DestroyRecord       | Can delete records                    |
| 7  | ContributeContents  | Can add content to containers         |

**For vector search filtering, we primarily care about `ViewDocument` permission.**

---

### **ACL String Formats**

Content Manager ACL can have different formats:

1. **Individual Users:**
   ```
   "DOMAIN\user1"
   ```

2. **Groups:**
   ```
   "Group:IT_Department"
   ```

3. **Locations:**
   ```
   "Location:Sydney"
   ```

4. **Combined (comma-separated):**
   ```
   "DOMAIN\user1, DOMAIN\user2, Group:IT_Department, Group:Managers"
   ```

The tool automatically parses all these formats!

---

## 🗄️ PostgreSQL Schema Design

After analysis, the tool generates 3 tables:

### **1. `record_acl` Table**
Stores ACL permissions for each record.

```sql
CREATE TABLE record_acl (
    id SERIAL PRIMARY KEY,
    record_uri BIGINT NOT NULL,
    record_title VARCHAR(500),
    permission_type VARCHAR(50) NOT NULL,

    allowed_users TEXT[],      -- Array of usernames
    allowed_groups TEXT[],     -- Array of group names
    allowed_locations TEXT[],  -- Array of locations

    raw_acl_string TEXT,
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW(),

    UNIQUE(record_uri, permission_type)
);

-- Indexes for fast lookups
CREATE INDEX idx_record_acl_users ON record_acl USING GIN(allowed_users);
CREATE INDEX idx_record_acl_groups ON record_acl USING GIN(allowed_groups);
```

**Usage in vector search:**
```sql
-- Find records accessible by user1 who belongs to groups ['IT_Department', 'Managers']
SELECT * FROM record_acl
WHERE permission_type = 'ViewDocument'
  AND (
    'DOMAIN\user1' = ANY(allowed_users)
    OR 'IT_Department' = ANY(allowed_groups)
    OR 'Managers' = ANY(allowed_groups)
  );
```

---

### **2. `user_group_cache` Table**
Caches Windows group memberships to avoid repeated AD queries.

```sql
CREATE TABLE user_group_cache (
    id SERIAL PRIMARY KEY,
    username VARCHAR(255) NOT NULL UNIQUE,
    groups TEXT[],  -- Array of group names user belongs to
    last_refreshed_at TIMESTAMP NOT NULL DEFAULT NOW()
);
```

---

### **3. `acl_sync_status` Table**
Tracks which records have been synced and when.

```sql
CREATE TABLE acl_sync_status (
    id SERIAL PRIMARY KEY,
    record_uri BIGINT NOT NULL UNIQUE,
    record_modified_date TIMESTAMP,
    last_acl_sync_at TIMESTAMP NOT NULL DEFAULT NOW(),
    sync_hash VARCHAR(64)  -- Hash of ACL to detect changes
);
```

---

## 🔄 Next Steps After Analysis

### **Option 1: Implement Solution 1 (Post-Search Validation)** ✅ Recommended
- Use the analysis results to understand your ACL structure
- Implement ACL filtering by validating each search result with Trim SDK
- No database changes needed
- Always accurate, no sync issues

### **Option 2: Implement Solution 2 (ACL in PostgreSQL)**
1. **Run the schema** generated by the tool on your PostgreSQL database
2. **Create a sync service** that:
   - Reads records from Content Manager
   - Extracts ACL using the patterns identified by the analysis
   - Stores ACL data in `record_acl` table
3. **Update vector search** to filter by `allowed_users` and `allowed_groups`
4. **Schedule regular sync** to keep ACL data current

---

## 📝 Logging

The tool provides detailed logging:

```
========== CONTENT MANAGER ACL ANALYSIS ==========
Analyzing ACL structure from 100 records
Connected to Content Manager. Fetching sample records...
   Analyzed 20 records so far...
   Analyzed 40 records so far...
...

========== ANALYSIS COMPLETE ==========
📊 Total Records Analyzed: 100
✅ Records with ACL: 95
⚠️ Records without ACL: 5
👥 Unique Users Found: 45
👨‍👩‍👧‍👦 Unique Groups Found: 12

Permission Type Frequency:
   ViewDocument: 95 records
   ViewMetadata: 95 records
   UpdateDocument: 20 records
   ...

========== ACL SAMPLES (First 3) ==========
Record: 12345 - Sample Document
Raw ACL: DOMAIN\user1, Group:IT_Department
   ViewDocument:
      Raw Value: DOMAIN\user1, Group:IT_Department
      Users: DOMAIN\user1
      Groups: IT_Department
      Locations:
```

---

## 🎯 Key Insights from Analysis

After running the tool, you'll know:

1. **ACL Coverage**: What percentage of records have ACL defined
2. **User/Group Distribution**: How many unique users and groups exist
3. **Permission Patterns**: Which permissions are most commonly used
4. **ACL Format**: How Content Manager stores ACL strings
5. **Database Design**: Exact schema needed for PostgreSQL sync

---

## ⚠️ Important Notes

1. **Windows Authentication Required**: The tool uses your Windows credentials to connect to Content Manager
2. **Sample Size**: Start with 100 records, increase if needed for comprehensive analysis
3. **No Database Changes**: This tool only READS from Content Manager (via Trim SDK), it doesn't modify anything
4. **No Direct DB Access**: Uses Trim SDK API only, never directly accesses Content Manager's database

---

## 🔒 Security

- Uses existing Windows Authentication
- Respects Content Manager's ACL when reading records
- Only analyzes records you already have access to
- No credentials stored or logged

---

## 📞 Support

If you encounter issues:
1. Check logs for detailed error messages
2. Verify Windows Authentication is working (`/api/AuthTest/info`)
3. Confirm Content Manager connection (`/api/ContentManager/test`)
4. Ensure you have access to Content Manager records

---

## Example Usage Flow

```bash
# 1. Analyze ACL structure
GET https://localhost:5001/api/AclAnalysis/analyze?sampleSize=200

# 2. Review the results (users, groups, permission patterns)

# 3. Generate PostgreSQL schema
GET https://localhost:5001/api/AclAnalysis/generate-schema?sampleSize=200

# 4. Copy the schema and run it on PostgreSQL database

# 5. Decide on implementation approach:
#    - Option 1: Post-search validation (simpler, recommended)
#    - Option 2: PostgreSQL ACL sync (better performance, more complex)
```

---

## 🎉 Ready to Proceed!

Once you've run the analysis and reviewed the results, confirm which implementation approach you'd like:

- **Solution 1**: I'll implement post-search ACL validation
- **Solution 2**: I'll help you build the PostgreSQL ACL sync service

Let me know which path you'd like to take!
