-- ============================================================
-- POSTGRESQL ACL SYNC SCHEMA FOR CONTENT MANAGER
-- Solution 2: PostgreSQL-based ACL filtering
-- ============================================================
--
-- Purpose: Sync ACL data from Content Manager to PostgreSQL
--          for high-performance ACL filtering in vector search
--
-- Based on analysis of Content Manager database (CM_DB1)
-- Tables: TSACLGROUP, TSACLGRPME, TSLOCATION, TSRECORD
--
-- Date: 2025-11-17
-- Database: PostgreSQL with pgvector extension
-- ============================================================

-- ============================================================
-- TABLE 1: CM_ACL_GROUPS
-- Mirrors: CM_DB1.dbo.TSACLGROUP
-- Purpose: Stores ACL group definitions from Content Manager
-- ============================================================

CREATE TABLE IF NOT EXISTS cm_acl_groups (
    -- Primary key (from Content Manager TSACLGROUP.uri)
    acl_group_id BIGINT PRIMARY KEY,

    -- ACL configuration hash (from Content Manager acgHash)
    acl_hash VARCHAR(64) NOT NULL,

    -- Timestamps for sync tracking
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Indexes for performance
CREATE INDEX idx_cm_acl_groups_hash ON cm_acl_groups(acl_hash);
CREATE INDEX idx_cm_acl_groups_synced ON cm_acl_groups(last_synced_at);

COMMENT ON TABLE cm_acl_groups IS 'ACL group definitions synced from Content Manager TSACLGROUP table';
COMMENT ON COLUMN cm_acl_groups.acl_group_id IS 'ACL Group URI from Content Manager';
COMMENT ON COLUMN cm_acl_groups.acl_hash IS 'Hash of ACL configuration for change detection';


-- ============================================================
-- TABLE 2: CM_LOCATIONS
-- Mirrors: CM_DB1.dbo.TSLOCATION
-- Purpose: Stores users, groups, and locations from Content Manager
-- ============================================================

CREATE TABLE IF NOT EXISTS cm_locations (
    -- Primary key (from Content Manager TSLOCATION.uri)
    location_id BIGINT PRIMARY KEY,

    -- Location name (from Content Manager lcName)
    -- Examples: "UKHAN2", "Pradeepa", "GroupUK"
    location_name VARCHAR(400) NOT NULL,

    -- Location type (from Content Manager lcType)
    -- 0 = System/Service account
    -- 2 = Group
    -- 4 = Individual user
    location_type SMALLINT NOT NULL,

    -- Nickname/short name (from Content Manager lcNickname)
    nickname VARCHAR(20),

    -- Active Directory DN (from Content Manager lcDirSyncDN)
    ad_dn VARCHAR(256),

    -- Whether AD sync is enabled (from Content Manager lcDirSyncEnabled)
    ad_sync_enabled BOOLEAN DEFAULT FALSE,

    -- Timestamps
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Indexes for performance
CREATE INDEX idx_cm_locations_name ON cm_locations(location_name);
CREATE INDEX idx_cm_locations_type ON cm_locations(location_type);
CREATE INDEX idx_cm_locations_name_type ON cm_locations(location_name, location_type);
CREATE INDEX idx_cm_locations_synced ON cm_locations(last_synced_at);

COMMENT ON TABLE cm_locations IS 'Users, groups, and locations synced from Content Manager TSLOCATION table';
COMMENT ON COLUMN cm_locations.location_id IS 'Location URI from Content Manager';
COMMENT ON COLUMN cm_locations.location_type IS '0=System, 2=Group, 4=User';


-- ============================================================
-- TABLE 3: CM_ACL_GROUP_MEMBERS
-- Mirrors: CM_DB1.dbo.TSACLGRPME
-- Purpose: Links ACL groups to users/groups (many-to-many)
-- ============================================================

CREATE TABLE IF NOT EXISTS cm_acl_group_members (
    id SERIAL PRIMARY KEY,

    -- ACL Group reference (from Content Manager agmAclGroup)
    acl_group_id BIGINT NOT NULL REFERENCES cm_acl_groups(acl_group_id) ON DELETE CASCADE,

    -- Location reference (from Content Manager agmLocation)
    location_id BIGINT NOT NULL REFERENCES cm_locations(location_id) ON DELETE CASCADE,

    -- Timestamp
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW(),

    -- Ensure unique group-location pairs
    UNIQUE(acl_group_id, location_id)
);

-- Indexes for performance
CREATE INDEX idx_cm_acl_members_group ON cm_acl_group_members(acl_group_id);
CREATE INDEX idx_cm_acl_members_location ON cm_acl_group_members(location_id);
CREATE INDEX idx_cm_acl_members_synced ON cm_acl_group_members(last_synced_at);

COMMENT ON TABLE cm_acl_group_members IS 'ACL group membership synced from Content Manager TSACLGRPME table';
COMMENT ON COLUMN cm_acl_group_members.acl_group_id IS 'Which ACL group';
COMMENT ON COLUMN cm_acl_group_members.location_id IS 'Which user/group is a member';


-- ============================================================
-- TABLE 4: CM_RECORD_ACL
-- Mirrors: CM_DB1.dbo.TSRECORD (ACL columns)
-- Purpose: Maps records to their ACL groups
-- ============================================================

CREATE TABLE IF NOT EXISTS cm_record_acl (
    -- Record URI (from Content Manager TSRECORD.uri)
    record_uri BIGINT PRIMARY KEY,

    -- ACL Group reference (from Content Manager rcAclGroupKey)
    -- NULL = Unrestricted (public access)
    -- NOT NULL = Restricted to ACL group members
    acl_group_id BIGINT REFERENCES cm_acl_groups(acl_group_id) ON DELETE SET NULL,

    -- Computed column for quick unrestricted check
    is_unrestricted BOOLEAN GENERATED ALWAYS AS (acl_group_id IS NULL) STORED,

    -- Timestamp
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Indexes for performance
CREATE INDEX idx_cm_record_acl_group ON cm_record_acl(acl_group_id);
CREATE INDEX idx_cm_record_acl_unrestricted ON cm_record_acl(is_unrestricted);
CREATE INDEX idx_cm_record_acl_synced ON cm_record_acl(last_synced_at);

COMMENT ON TABLE cm_record_acl IS 'Record to ACL group mapping synced from Content Manager TSRECORD table';
COMMENT ON COLUMN cm_record_acl.record_uri IS 'Record URI from Content Manager';
COMMENT ON COLUMN cm_record_acl.acl_group_id IS 'ACL Group (NULL = unrestricted)';
COMMENT ON COLUMN cm_record_acl.is_unrestricted IS 'TRUE if record is publicly accessible';


-- ============================================================
-- TABLE 5: CM_USER_GROUPS (Optional - for performance)
-- Purpose: Cache user-to-group memberships from Windows AD
-- ============================================================

CREATE TABLE IF NOT EXISTS cm_user_groups (
    id SERIAL PRIMARY KEY,

    -- User location ID
    user_location_id BIGINT NOT NULL REFERENCES cm_locations(location_id) ON DELETE CASCADE,

    -- Group location ID
    group_location_id BIGINT NOT NULL REFERENCES cm_locations(location_id) ON DELETE CASCADE,

    -- Timestamp for cache refresh
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW(),

    -- Ensure unique user-group pairs
    UNIQUE(user_location_id, group_location_id),

    -- Ensure we don't link a user to themselves
    CHECK (user_location_id != group_location_id)
);

-- Indexes for performance
CREATE INDEX idx_cm_user_groups_user ON cm_user_groups(user_location_id);
CREATE INDEX idx_cm_user_groups_group ON cm_user_groups(group_location_id);
CREATE INDEX idx_cm_user_groups_synced ON cm_user_groups(last_synced_at);

COMMENT ON TABLE cm_user_groups IS 'User to group membership cache from Windows AD';
COMMENT ON COLUMN cm_user_groups.user_location_id IS 'User (lcType=4)';
COMMENT ON COLUMN cm_user_groups.group_location_id IS 'Group (lcType=2)';


-- ============================================================
-- TABLE 6: CM_ACL_SYNC_LOG
-- Purpose: Track sync operations and status
-- ============================================================

CREATE TABLE IF NOT EXISTS cm_acl_sync_log (
    id SERIAL PRIMARY KEY,

    -- Sync type
    sync_type VARCHAR(50) NOT NULL,  -- 'full', 'incremental', 'acl_only'

    -- Counts
    records_synced INT NOT NULL DEFAULT 0,
    acl_groups_synced INT NOT NULL DEFAULT 0,
    locations_synced INT NOT NULL DEFAULT 0,
    members_synced INT NOT NULL DEFAULT 0,

    -- Timing
    started_at TIMESTAMP NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMP,
    duration_seconds INT,

    -- Status
    status VARCHAR(20) NOT NULL DEFAULT 'running',  -- 'running', 'completed', 'failed'
    error_message TEXT,

    -- Additional info
    sync_details JSONB
);

-- Indexes
CREATE INDEX idx_cm_sync_log_status ON cm_acl_sync_log(status);
CREATE INDEX idx_cm_sync_log_started ON cm_acl_sync_log(started_at DESC);
CREATE INDEX idx_cm_sync_log_type ON cm_acl_sync_log(sync_type);

COMMENT ON TABLE cm_acl_sync_log IS 'Log of ACL sync operations from Content Manager';


-- ============================================================
-- VIEW 1: USER_ACCESSIBLE_RECORDS
-- Purpose: Quick lookup of which records a user can access
-- ============================================================

CREATE OR REPLACE VIEW v_user_accessible_records AS
WITH user_direct_access AS (
    -- Records where user is directly in the ACL group
    SELECT DISTINCT
        ra.record_uri,
        loc.location_name AS username
    FROM cm_record_acl ra
    INNER JOIN cm_acl_group_members gm ON ra.acl_group_id = gm.acl_group_id
    INNER JOIN cm_locations loc ON gm.location_id = loc.location_id
    WHERE loc.location_type = 4  -- Users only
),
user_group_access AS (
    -- Records where user's group is in the ACL group
    SELECT DISTINCT
        ra.record_uri,
        user_loc.location_name AS username
    FROM cm_record_acl ra
    INNER JOIN cm_acl_group_members gm ON ra.acl_group_id = gm.acl_group_id
    INNER JOIN cm_locations grp_loc ON gm.location_id = grp_loc.location_id
    INNER JOIN cm_user_groups ug ON grp_loc.location_id = ug.group_location_id
    INNER JOIN cm_locations user_loc ON ug.user_location_id = user_loc.location_id
    WHERE grp_loc.location_type = 2  -- Groups only
      AND user_loc.location_type = 4  -- Users only
),
unrestricted_records AS (
    -- All unrestricted records (accessible to everyone)
    SELECT
        record_uri,
        NULL::VARCHAR AS username  -- NULL means accessible to all
    FROM cm_record_acl
    WHERE is_unrestricted = TRUE
)
SELECT record_uri, username, 'direct' AS access_type FROM user_direct_access
UNION ALL
SELECT record_uri, username, 'group' AS access_type FROM user_group_access
UNION ALL
SELECT record_uri, username, 'unrestricted' AS access_type FROM unrestricted_records;

COMMENT ON VIEW v_user_accessible_records IS 'Shows which users can access which records (includes unrestricted, direct, and group-based access)';


-- ============================================================
-- SAMPLE SYNC QUERIES (Run from C# Sync Service)
-- ============================================================

-- ============================================================
-- SYNC QUERY 1: Sync ACL Groups from Content Manager
-- ============================================================

/*
INSERT INTO cm_acl_groups (acl_group_id, acl_hash, last_synced_at)
SELECT
    uri AS acl_group_id,
    acgHash AS acl_hash,
    NOW() AS last_synced_at
FROM [CM_DB1].dbo.TSACLGROUP
ON CONFLICT (acl_group_id)
DO UPDATE SET
    acl_hash = EXCLUDED.acl_hash,
    last_synced_at = NOW();
*/


-- ============================================================
-- SYNC QUERY 2: Sync Locations (Users & Groups) from Content Manager
-- ============================================================

/*
INSERT INTO cm_locations (location_id, location_name, location_type, nickname, ad_dn, ad_sync_enabled, last_synced_at)
SELECT
    uri AS location_id,
    lcName AS location_name,
    lcType AS location_type,
    TRIM(lcNickname) AS nickname,
    lcDirSyncDN AS ad_dn,
    CASE WHEN lcDirSyncEnabled = '1' THEN TRUE ELSE FALSE END AS ad_sync_enabled,
    NOW() AS last_synced_at
FROM [CM_DB1].dbo.TSLOCATION
WHERE lcType IN (0, 2, 4)  -- System, Groups, Users
ON CONFLICT (location_id)
DO UPDATE SET
    location_name = EXCLUDED.location_name,
    location_type = EXCLUDED.location_type,
    nickname = EXCLUDED.nickname,
    ad_dn = EXCLUDED.ad_dn,
    ad_sync_enabled = EXCLUDED.ad_sync_enabled,
    last_synced_at = NOW();
*/


-- ============================================================
-- SYNC QUERY 3: Sync ACL Group Members from Content Manager
-- ============================================================

/*
INSERT INTO cm_acl_group_members (acl_group_id, location_id, last_synced_at)
SELECT
    agmAclGroup AS acl_group_id,
    agmLocation AS location_id,
    NOW() AS last_synced_at
FROM [CM_DB1].dbo.TSACLGRPME
ON CONFLICT (acl_group_id, location_id)
DO UPDATE SET
    last_synced_at = NOW();
*/


-- ============================================================
-- SYNC QUERY 4: Sync Record ACL Mappings from Content Manager
-- ============================================================

/*
INSERT INTO cm_record_acl (record_uri, acl_group_id, last_synced_at)
SELECT
    uri AS record_uri,
    rcAclGroupKey AS acl_group_id,
    NOW() AS last_synced_at
FROM [CM_DB1].dbo.TSRECORD
ON CONFLICT (record_uri)
DO UPDATE SET
    acl_group_id = EXCLUDED.acl_group_id,
    last_synced_at = NOW();
*/


-- ============================================================
-- ACL FILTERING QUERY FOR VECTOR SEARCH
-- Use this in your RecordSearchService to filter results
-- ============================================================

/*
-- Get accessible record URIs for current user
-- Replace 'UKHAN2' with actual current username

WITH user_info AS (
    -- Get current user's location info
    SELECT location_id, location_name
    FROM cm_locations
    WHERE location_name = 'UKHAN2'  -- Current user
      AND location_type = 4          -- User type
    LIMIT 1
),
user_groups AS (
    -- Get all groups this user belongs to
    SELECT ug.group_location_id
    FROM cm_user_groups ug
    INNER JOIN user_info ui ON ug.user_location_id = ui.location_id
)
SELECT DISTINCT
    e.record_uri,
    e.record_title,
    e.vector,
    e.record_type,
    e.date_created
FROM embeddings e
INNER JOIN cm_record_acl ra ON e.record_uri = ra.record_uri
WHERE (
    -- Option 1: Record is unrestricted (public)
    ra.is_unrestricted = TRUE
    OR
    -- Option 2: User has direct access
    EXISTS (
        SELECT 1
        FROM cm_acl_group_members gm
        INNER JOIN user_info ui ON gm.location_id = ui.location_id
        WHERE gm.acl_group_id = ra.acl_group_id
    )
    OR
    -- Option 3: User belongs to a group with access
    EXISTS (
        SELECT 1
        FROM cm_acl_group_members gm
        INNER JOIN user_groups ug ON gm.location_id = ug.group_location_id
        WHERE gm.acl_group_id = ra.acl_group_id
    )
)
ORDER BY e.vector <=> :query_embedding  -- pgvector distance
LIMIT 20;
*/


-- ============================================================
-- HELPER QUERIES FOR TESTING AND DEBUGGING
-- ============================================================

-- Query 1: Check which users have access to a specific record
/*
SELECT
    loc.location_name AS username,
    loc.location_type,
    CASE
        WHEN ra.is_unrestricted THEN 'Unrestricted'
        WHEN gm.location_id IS NOT NULL THEN 'Direct Access'
        ELSE 'Group Access'
    END AS access_type
FROM cm_record_acl ra
LEFT JOIN cm_acl_group_members gm ON ra.acl_group_id = gm.acl_group_id
LEFT JOIN cm_locations loc ON gm.location_id = loc.location_id
WHERE ra.record_uri = 2  -- CM9.4_ServiceAPI
  AND (ra.is_unrestricted = TRUE OR loc.location_id IS NOT NULL);
*/

-- Query 2: List all ACL groups and their members
/*
SELECT
    acl.acl_group_id,
    acl.acl_hash,
    loc.location_name,
    loc.location_type,
    CASE loc.location_type
        WHEN 0 THEN 'System'
        WHEN 2 THEN 'Group'
        WHEN 4 THEN 'User'
        ELSE 'Unknown'
    END AS type_name
FROM cm_acl_groups acl
INNER JOIN cm_acl_group_members gm ON acl.acl_group_id = gm.acl_group_id
INNER JOIN cm_locations loc ON gm.location_id = loc.location_id
ORDER BY acl.acl_group_id, loc.location_type, loc.location_name;
*/

-- Query 3: Count records by ACL type
/*
SELECT
    CASE
        WHEN is_unrestricted THEN 'Unrestricted'
        ELSE 'ACL Restricted'
    END AS acl_type,
    COUNT(*) AS record_count
FROM cm_record_acl
GROUP BY is_unrestricted;
*/

-- Query 4: Find records accessible to a specific user
/*
SELECT
    record_uri,
    access_type
FROM v_user_accessible_records
WHERE username = 'UKHAN2' OR username IS NULL  -- NULL = unrestricted
ORDER BY record_uri;
*/


-- ============================================================
-- MAINTENANCE QUERIES
-- ============================================================

-- Clean up orphaned records (records not in embeddings table)
/*
DELETE FROM cm_record_acl
WHERE record_uri NOT IN (SELECT record_uri FROM embeddings);
*/

-- Refresh ACL sync statistics
/*
SELECT
    'ACL Groups' AS entity,
    COUNT(*) AS total,
    MAX(last_synced_at) AS last_sync
FROM cm_acl_groups
UNION ALL
SELECT
    'Locations' AS entity,
    COUNT(*) AS total,
    MAX(last_synced_at) AS last_sync
FROM cm_locations
UNION ALL
SELECT
    'ACL Group Members' AS entity,
    COUNT(*) AS total,
    MAX(last_synced_at) AS last_sync
FROM cm_acl_group_members
UNION ALL
SELECT
    'Record ACL Mappings' AS entity,
    COUNT(*) AS total,
    MAX(last_synced_at) AS last_sync
FROM cm_record_acl;
*/


-- ============================================================
-- PERFORMANCE TIPS
-- ============================================================

-- 1. Regularly VACUUM and ANALYZE tables
-- VACUUM ANALYZE cm_acl_groups;
-- VACUUM ANALYZE cm_locations;
-- VACUUM ANALYZE cm_acl_group_members;
-- VACUUM ANALYZE cm_record_acl;

-- 2. Monitor index usage
/*
SELECT
    schemaname,
    tablename,
    indexname,
    idx_scan AS index_scans
FROM pg_stat_user_indexes
WHERE schemaname = 'public'
  AND tablename LIKE 'cm_%'
ORDER BY idx_scan DESC;
*/

-- 3. Check table sizes
/*
SELECT
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size
FROM pg_tables
WHERE schemaname = 'public'
  AND tablename LIKE 'cm_%'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;
*/


-- ============================================================
-- END OF SCHEMA
-- ============================================================

-- Next Steps:
-- 1. Run this schema on your PostgreSQL database
-- 2. Implement C# sync service to populate data from Content Manager
-- 3. Update RecordSearchService to use ACL filtering query
-- 4. Test with different users and ACL scenarios
-- 5. Monitor performance and adjust indexes as needed
