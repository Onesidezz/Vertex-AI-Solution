using DocumentProcessingAPI.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessingAPI.API.Controllers
{
    /// <summary>
    /// Controller for analyzing Content Manager ACL structure
    /// Used to understand how to sync ACL data to PostgreSQL
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Requires Windows authentication
    public class AclAnalysisController : ControllerBase
    {
        private readonly AclAnalysisService _aclAnalysisService;
        private readonly ILogger<AclAnalysisController> _logger;

        public AclAnalysisController(
            AclAnalysisService aclAnalysisService,
            ILogger<AclAnalysisController> logger)
        {
            _aclAnalysisService = aclAnalysisService;
            _logger = logger;
        }

        /// <summary>
        /// Analyze ACL structure from Content Manager
        /// GET /api/AclAnalysis/analyze?sampleSize=100
        /// </summary>
        [HttpGet("analyze")]
        public async Task<IActionResult> AnalyzeAclStructure([FromQuery] int sampleSize = 100)
        {
            try
            {
                _logger.LogInformation("ACL analysis requested by {User} for {SampleSize} records",
                    User.Identity?.Name, sampleSize);

                var result = await _aclAnalysisService.AnalyzeAclStructureAsync(sampleSize);

                return Ok(new
                {
                    Success = true,
                    Summary = new
                    {
                        TotalRecordsAnalyzed = result.TotalRecordsAnalyzed,
                        RecordsWithAcl = result.RecordsWithAcl,
                        RecordsWithoutAcl = result.RecordsWithoutAcl,
                        UniqueUsersCount = result.UniqueUsersFound.Count,
                        UniqueGroupsCount = result.UniqueGroupsFound.Count,
                        UniqueUsers = result.UniqueUsersFound.Take(50).ToList(),
                        UniqueGroups = result.UniqueGroupsFound.Take(50).ToList(),
                        PermissionTypes = result.PermissionTypes
                    },
                    Samples = result.AclSamples.Select(s => new
                    {
                        s.RecordUri,
                        s.RecordTitle,
                        s.RawAclString,
                        Permissions = s.Permissions.Select(p => new
                        {
                            PermissionName = p.Key,
                            p.Value.RawValue,
                            p.Value.Users,
                            p.Value.Groups,
                            p.Value.Locations
                        }).ToList()
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze ACL structure");
                return StatusCode(500, new
                {
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Generate PostgreSQL schema for ACL sync
        /// GET /api/AclAnalysis/generate-schema?sampleSize=100
        /// </summary>
        [HttpGet("generate-schema")]
        public async Task<IActionResult> GeneratePostgreSqlSchema([FromQuery] int sampleSize = 100)
        {
            try
            {
                _logger.LogInformation("PostgreSQL schema generation requested by {User}",
                    User.Identity?.Name);

                // First analyze the ACL structure
                var analysisResult = await _aclAnalysisService.AnalyzeAclStructureAsync(sampleSize);

                // Generate schema
                var schema = _aclAnalysisService.GeneratePostgreSqlSchema(analysisResult);

                return Ok(new
                {
                    Success = true,
                    Schema = schema,
                    AnalysisSummary = new
                    {
                        TotalRecordsAnalyzed = analysisResult.TotalRecordsAnalyzed,
                        RecordsWithAcl = analysisResult.RecordsWithAcl,
                        UniqueUsersCount = analysisResult.UniqueUsersFound.Count,
                        UniqueGroupsCount = analysisResult.UniqueGroupsFound.Count
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate PostgreSQL schema");
                return StatusCode(500, new
                {
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get detailed ACL information for a specific record
        /// GET /api/AclAnalysis/record/{uri}
        /// </summary>
        [HttpGet("record/{uri}")]
        public async Task<IActionResult> GetRecordAcl(long uri)
        {
            try
            {
                _logger.LogInformation("ACL details requested for record {Uri} by {User}",
                    uri, User.Identity?.Name);

                // Analyze just this one record
                var analysisResult = await _aclAnalysisService.AnalyzeAclStructureAsync(1);

                if (!analysisResult.AclSamples.Any())
                {
                    return NotFound(new
                    {
                        Success = false,
                        Error = "Record not found or has no ACL"
                    });
                }

                var sample = analysisResult.AclSamples.First();

                return Ok(new
                {
                    Success = true,
                    RecordUri = sample.RecordUri,
                    RecordTitle = sample.RecordTitle,
                    RawAclString = sample.RawAclString,
                    Permissions = sample.Permissions.Select(p => new
                    {
                        PermissionName = p.Key,
                        p.Value.RawValue,
                        p.Value.Users,
                        p.Value.Groups,
                        p.Value.Locations
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get ACL for record {Uri}", uri);
                return StatusCode(500, new
                {
                    Success = false,
                    Error = ex.Message
                });
            }
        }
    }
}
