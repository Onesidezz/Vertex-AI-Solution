using DocumentProcessingAPI.Core.DTOs;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TRIM.SDK;

namespace DocumentProcessingAPI.Infrastructure.Services
{
    /// <summary>
    /// Service to analyze Content Manager ACL structure
    /// Extracts permission data to understand how to sync to PostgreSQL
    /// </summary>
    public class AclAnalysisService
    {
        private readonly ContentManagerServices _contentManagerServices;
        private readonly ILogger<AclAnalysisService> _logger;

        public AclAnalysisService(
            ContentManagerServices contentManagerServices,
            ILogger<AclAnalysisService> logger)
        {
            _contentManagerServices = contentManagerServices;
            _logger = logger;
        }

        /// <summary>
        /// Analyze ACL structure from Content Manager records
        /// Extracts sample ACL data to understand permissions model
        /// </summary>
        public async Task<AclAnalysisResult> AnalyzeAclStructureAsync(int sampleSize = 100)
        {
            _logger.LogInformation("========== CONTENT MANAGER ACL ANALYSIS ==========");
            _logger.LogInformation("Analyzing ACL structure from {SampleSize} records", sampleSize);

            var result = new AclAnalysisResult
            {
                TotalRecordsAnalyzed = 0,
                RecordsWithAcl = 0,
                RecordsWithoutAcl = 0,
                AclSamples = new List<AclSample>(),
                UniqueUsersFound = new HashSet<string>(),
                UniqueGroupsFound = new HashSet<string>(),
                PermissionTypes = new Dictionary<string, int>()
            };

            try
            {
                var database = await _contentManagerServices.GetDatabaseAsync();

                _logger.LogInformation("Connected to Content Manager. Fetching sample records...");

                // Get sample records from Content Manager
                TrimMainObjectSearch search = new TrimMainObjectSearch(database, BaseObjectTypes.Record);
                search.SetSearchString("number:*"); // Get all records

                var recordCount = 0;
                foreach (Record record in search)
                {
                    if (recordCount >= sampleSize)
                        break;

                    try
                    {
                        result.TotalRecordsAnalyzed++;

                        var acl = record.AccessControlList;

                        if (acl == null)
                        {
                            result.RecordsWithoutAcl++;
                            _logger.LogDebug("Record {Uri} has no ACL", record.Uri.Value);
                            continue;
                        }

                        result.RecordsWithAcl++;

                        // Extract all permission types (1-7 based on Content Manager SDK)
                        var aclSample = new AclSample
                        {
                            RecordUri = record.Uri.Value,
                            RecordTitle = record.Title,
                            RawAclString = acl.ToString(),
                            Permissions = new Dictionary<string, AclPermissionDetail>()
                        };

                        // Permission Types (from Trim SDK documentation):
                        // 1 = ViewDocument, 2 = ViewMetadata, 3 = UpdateDocument, 4 = UpdateMetadata,
                        // 5 = ModifyAccess, 6 = DestroyRecord, 7 = ContributeContents
                        var permissionNames = new Dictionary<int, string>
                        {
                            { 1, "ViewDocument" },
                            { 2, "ViewMetadata" },
                            { 3, "UpdateDocument" },
                            { 4, "UpdateMetadata" },
                            { 5, "ModifyAccess" },
                            { 6, "DestroyRecord" },
                            { 7, "ContributeContents" }
                        };

                        foreach (var perm in permissionNames)
                        {
                            try
                            {
                                var permissionValue = acl.get_AsString(perm.Key);

                                if (!string.IsNullOrWhiteSpace(permissionValue))
                                {
                                    var permDetail = ParsePermissionString(permissionValue);
                                    aclSample.Permissions[perm.Value] = permDetail;

                                    // Track permission type frequency
                                    if (!result.PermissionTypes.ContainsKey(perm.Value))
                                        result.PermissionTypes[perm.Value] = 0;
                                    result.PermissionTypes[perm.Value]++;

                                    // Collect unique users and groups
                                    foreach (var user in permDetail.Users)
                                        result.UniqueUsersFound.Add(user);
                                    foreach (var group in permDetail.Groups)
                                        result.UniqueGroupsFound.Add(group);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to extract permission {PermName} for record {Uri}",
                                    perm.Value, record.Uri.Value);
                            }
                        }

                        // Store sample (only first 10 for detailed logging)
                        if (result.AclSamples.Count < 10)
                        {
                            result.AclSamples.Add(aclSample);
                        }

                        recordCount++;

                        if (recordCount % 20 == 0)
                        {
                            _logger.LogInformation("   Analyzed {Count} records so far...", recordCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to analyze ACL for record: {Error}", ex.Message);
                    }
                }

                _logger.LogInformation("========== ANALYSIS COMPLETE ==========");
                _logger.LogInformation("📊 Total Records Analyzed: {Total}", result.TotalRecordsAnalyzed);
                _logger.LogInformation("✅ Records with ACL: {WithAcl}", result.RecordsWithAcl);
                _logger.LogInformation("⚠️ Records without ACL: {WithoutAcl}", result.RecordsWithoutAcl);
                _logger.LogInformation("👥 Unique Users Found: {Users}", result.UniqueUsersFound.Count);
                _logger.LogInformation("👨‍👩‍👧‍👦 Unique Groups Found: {Groups}", result.UniqueGroupsFound.Count);
                _logger.LogInformation("");
                _logger.LogInformation("Permission Type Frequency:");
                foreach (var perm in result.PermissionTypes.OrderByDescending(p => p.Value))
                {
                    _logger.LogInformation("   {PermName}: {Count} records", perm.Key, perm.Value);
                }

                // Log sample ACLs for understanding structure
                _logger.LogInformation("");
                _logger.LogInformation("========== ACL SAMPLES (First 3) ==========");
                foreach (var sample in result.AclSamples.Take(3))
                {
                    _logger.LogInformation("");
                    _logger.LogInformation("Record: {Uri} - {Title}", sample.RecordUri, sample.RecordTitle);
                    _logger.LogInformation("Raw ACL: {RawAcl}", sample.RawAclString);

                    foreach (var perm in sample.Permissions)
                    {
                        _logger.LogInformation("   {PermName}:", perm.Key);
                        _logger.LogInformation("      Raw Value: {RawValue}", perm.Value.RawValue);
                        _logger.LogInformation("      Users: {Users}", string.Join(", ", perm.Value.Users));
                        _logger.LogInformation("      Groups: {Groups}", string.Join(", ", perm.Value.Groups));
                        _logger.LogInformation("      Locations: {Locations}", string.Join(", ", perm.Value.Locations));
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze ACL structure");
                throw;
            }
        }

        /// <summary>
        /// Parse permission string to extract users, groups, and locations
        /// </summary>
        private AclPermissionDetail ParsePermissionString(string permissionValue)
        {
            var detail = new AclPermissionDetail
            {
                RawValue = permissionValue,
                Users = new List<string>(),
                Groups = new List<string>(),
                Locations = new List<string>()
            };

            if (string.IsNullOrWhiteSpace(permissionValue))
                return detail;

            // Common patterns in Content Manager ACL strings:
            // 1. "Username" - individual user
            // 2. "DOMAIN\Username" - domain user
            // 3. "Group:GroupName" - group
            // 4. "Location:LocationName" - location
            // 5. Comma-separated list of above

            var parts = permissionValue.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var trimmed = part.Trim();

                if (trimmed.StartsWith("Group:", StringComparison.OrdinalIgnoreCase))
                {
                    // It's a group
                    var groupName = trimmed.Substring(6).Trim();
                    if (!string.IsNullOrWhiteSpace(groupName))
                        detail.Groups.Add(groupName);
                }
                else if (trimmed.StartsWith("Location:", StringComparison.OrdinalIgnoreCase))
                {
                    // It's a location
                    var locationName = trimmed.Substring(9).Trim();
                    if (!string.IsNullOrWhiteSpace(locationName))
                        detail.Locations.Add(locationName);
                }
                else if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    // Assume it's a user (could be DOMAIN\User or just User)
                    detail.Users.Add(trimmed);
                }
            }

            return detail;
        }

        /// <summary>
        /// Generate PostgreSQL table schema based on ACL analysis
        /// </summary>
        public string GeneratePostgreSqlSchema(AclAnalysisResult analysisResult)
        {
            _logger.LogInformation("========== GENERATING POSTGRESQL SCHEMA ==========");

            var schema = @"
-- ============================================================
-- CONTENT MANAGER ACL SYNC TABLES FOR POSTGRESQL
-- Generated based on analysis of Content Manager ACL structure
-- ============================================================

-- Table 1: Record ACL Summary
-- Stores which users/groups have ViewDocument permission for each record
CREATE TABLE IF NOT EXISTS record_acl (
    id SERIAL PRIMARY KEY,
    record_uri BIGINT NOT NULL,
    record_title VARCHAR(500),
    permission_type VARCHAR(50) NOT NULL, -- e.g., 'ViewDocument', 'UpdateDocument'

    -- ACL data (JSON arrays for flexibility)
    allowed_users TEXT[], -- Array of usernames who have this permission
    allowed_groups TEXT[], -- Array of group names who have this permission
    allowed_locations TEXT[], -- Array of location names (if applicable)

    -- Raw ACL for debugging
    raw_acl_string TEXT,

    -- Metadata
    last_synced_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),

    -- Indexes for fast lookups
    UNIQUE(record_uri, permission_type)
);

CREATE INDEX idx_record_acl_uri ON record_acl(record_uri);
CREATE INDEX idx_record_acl_permission ON record_acl(permission_type);
CREATE INDEX idx_record_acl_users ON record_acl USING GIN(allowed_users); -- GIN index for array search
CREATE INDEX idx_record_acl_groups ON record_acl USING GIN(allowed_groups);

-- Table 2: User to Groups Mapping Cache
-- Caches which groups each user belongs to (from Active Directory)
-- This avoids repeated AD queries during search
CREATE TABLE IF NOT EXISTS user_group_cache (
    id SERIAL PRIMARY KEY,
    username VARCHAR(255) NOT NULL UNIQUE,
    display_name VARCHAR(500),
    email VARCHAR(500),

    -- Groups this user belongs to
    groups TEXT[], -- Array of group names

    -- Cache metadata
    last_refreshed_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_user_group_username ON user_group_cache(username);
CREATE INDEX idx_user_group_groups ON user_group_cache USING GIN(groups);

-- Table 3: ACL Sync Status
-- Tracks sync progress and detects records that need re-syncing
CREATE TABLE IF NOT EXISTS acl_sync_status (
    id SERIAL PRIMARY KEY,
    record_uri BIGINT NOT NULL UNIQUE,
    record_modified_date TIMESTAMP,
    last_acl_sync_at TIMESTAMP NOT NULL DEFAULT NOW(),
    sync_hash VARCHAR(64), -- Hash of ACL data to detect changes

    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_acl_sync_uri ON acl_sync_status(record_uri);
CREATE INDEX idx_acl_sync_modified ON acl_sync_status(record_modified_date);

-- ============================================================
-- ANALYSIS SUMMARY
-- ============================================================
-- Total Records Analyzed: " + analysisResult.TotalRecordsAnalyzed + @"
-- Records with ACL: " + analysisResult.RecordsWithAcl + @"
-- Unique Users Found: " + analysisResult.UniqueUsersFound.Count + @"
-- Unique Groups Found: " + analysisResult.UniqueGroupsFound.Count + @"
--
-- Users Found:
" + string.Join("\n", analysisResult.UniqueUsersFound.Take(20).Select(u => "-- - " + u)) + @"
" + (analysisResult.UniqueUsersFound.Count > 20 ? "-- ... and " + (analysisResult.UniqueUsersFound.Count - 20) + " more" : "") + @"
--
-- Groups Found:
" + string.Join("\n", analysisResult.UniqueGroupsFound.Take(20).Select(g => "-- - " + g)) + @"
" + (analysisResult.UniqueGroupsFound.Count > 20 ? "-- ... and " + (analysisResult.UniqueGroupsFound.Count - 20) + " more" : "") + @"
-- ============================================================
";

            _logger.LogInformation("PostgreSQL schema generated successfully");
            return schema;
        }
    }

    #region DTOs

    public class AclAnalysisResult
    {
        public int TotalRecordsAnalyzed { get; set; }
        public int RecordsWithAcl { get; set; }
        public int RecordsWithoutAcl { get; set; }
        public List<AclSample> AclSamples { get; set; }
        public HashSet<string> UniqueUsersFound { get; set; }
        public HashSet<string> UniqueGroupsFound { get; set; }
        public Dictionary<string, int> PermissionTypes { get; set; }
    }

    public class AclSample
    {
        public long RecordUri { get; set; }
        public string RecordTitle { get; set; }
        public string RawAclString { get; set; }
        public Dictionary<string, AclPermissionDetail> Permissions { get; set; }
    }

    public class AclPermissionDetail
    {
        public string RawValue { get; set; }
        public List<string> Users { get; set; }
        public List<string> Groups { get; set; }
        public List<string> Locations { get; set; }
    }

    #endregion
}
