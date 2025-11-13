using DocumentProcessingAPI.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessingAPI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly PgVectorService _pgVectorService;
        private readonly ILogger<DiagnosticsController> _logger;

        public DiagnosticsController(PgVectorService pgVectorService, ILogger<DiagnosticsController> logger)
        {
            _pgVectorService = pgVectorService;
            _logger = logger;
        }

        [HttpGet("check-dates")]
        public async Task<ActionResult> CheckDates()
        {
            try
            {
                var stats = await _pgVectorService.GetCollectionStatsAsync();

                // Get some sample records to check their dates
                var sampleResults = await _pgVectorService.GetSampleRecordsAsync(20);

                var diagnostics = new
                {
                    TotalRecords = stats.TotalPoints,
                    SampleRecords = sampleResults.Select(r => new
                    {
                        RecordUri = r.metadata.ContainsKey("record_uri") ? r.metadata["record_uri"] : null,
                        RecordTitle = r.metadata.ContainsKey("record_title") ? r.metadata["record_title"] : null,
                        DateCreated = r.metadata.ContainsKey("date_created") ? r.metadata["date_created"] : null,
                        FileType = r.metadata.ContainsKey("file_type") ? r.metadata["file_type"] : null,
                        IndexedAt = r.metadata.ContainsKey("indexed_at") ? r.metadata["indexed_at"] : null
                    }).ToList()
                };

                return Ok(diagnostics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get diagnostics");
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }
}
