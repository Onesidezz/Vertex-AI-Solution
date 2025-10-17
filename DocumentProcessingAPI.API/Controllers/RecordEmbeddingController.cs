using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessingAPI.API.Controllers
{
    /// <summary>
    /// API controller for Content Manager record embedding and search operations
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class RecordEmbeddingController : ControllerBase
    {
        private readonly IRecordEmbeddingService _recordEmbeddingService;
        private readonly IRecordSearchService _recordSearchService;
        private readonly ILogger<RecordEmbeddingController> _logger;

        public RecordEmbeddingController(
            IRecordEmbeddingService recordEmbeddingService,
            IRecordSearchService recordSearchService,
            ILogger<RecordEmbeddingController> logger)
        {
            _recordEmbeddingService = recordEmbeddingService;
            _recordSearchService = recordSearchService;
            _logger = logger;
        }

        /// <summary>
        /// Process all records from Content Manager and generate embeddings
        /// </summary>
        /// <param name="searchString">TRIM search string (default: "*" for all records)</param>
        /// <returns>Number of records processed</returns>
        [HttpPost("process-all")]
        public async Task<ActionResult<ProcessRecordsResponseDto>> ProcessAllRecords([FromQuery] string searchString = "*")
        {
            try
            {
                _logger.LogInformation("API: Processing all records with search: {SearchString}", searchString);

                var processedCount = await _recordEmbeddingService.ProcessAllRecordsAsync(searchString);

                return Ok(new ProcessRecordsResponseDto
                {
                    Success = true,
                    ProcessedCount = processedCount,
                    Message = $"Successfully processed {processedCount} records"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Failed to process all records");
                return StatusCode(500, new ProcessRecordsResponseDto
                {
                    Success = false,
                    ProcessedCount = 0,
                    Message = $"Error processing records: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Search records using natural language query with comprehensive date filtering support
        ///
        /// Supports date queries for:
        /// - Specific dates: "today", "yesterday", "October 9, 2024"
        /// - Date ranges: "last week", "last 3 months", "last 2 years"
        /// - Weeks: "this week", "week 1", "week 42 of 2024", "week of October 3"
        /// - Months: "this month", "last month", "October 2024", "last October"
        /// - Years: "this year", "last year", "2024", "year 2023"
        /// - Quarters: "Q1 2024", "Q2", "first quarter", "second quarter 2023"
        /// - Sorting: "earliest", "latest", "most recent", "oldest"
        /// - Combined: "Excel files from Q1 2024", "Word documents created in October 2024"
        /// </summary>
        /// <param name="request">Search request</param>
        [HttpPost("search")]
        public async Task<ActionResult<RecordSearchResponseDto>> SearchRecords([FromBody] RecordSearchRequestDto request)
        {
            try
            {
                _logger.LogInformation("API: Searching records with query: {Query}", request.Query);

                var results = await _recordSearchService.SearchRecordsAsync(
                    request.Query,
                    request.MetadataFilters,
                    request.TopK,
                    request.MinimumScore);

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Search failed for query: {Query}", request.Query);
                return StatusCode(500, new RecordSearchResponseDto
                {
                    Query = request.Query,
                    Results = new List<RecordSearchResultDto>(),
                    TotalResults = 0,
                    QueryTime = 0
                });
            }
        }

        /// <summary>
        /// Delete all embeddings (chunks) for a specific Content Manager record URI
        /// This removes all vector data associated with the record from the vector database
        /// </summary>
        /// <param name="recordUri">The Content Manager record URI to delete</param>
        /// <returns>Number of embeddings deleted</returns>
        [HttpDelete("record/{recordUri}")]
        public async Task<ActionResult<DeleteRecordResponseDto>> DeleteRecordEmbeddings(long recordUri)
        {
            try
            {
                _logger.LogInformation("API: Deleting embeddings for record URI: {RecordUri}", recordUri);

                var deletedCount = await _recordEmbeddingService.DeleteRecordEmbeddingsAsync(recordUri);

                if (deletedCount > 0)
                {
                    return Ok(new DeleteRecordResponseDto
                    {
                        Success = true,
                        RecordUri = recordUri,
                        DeletedChunks = deletedCount,
                        Message = $"Successfully deleted {deletedCount} embedding chunks for record URI {recordUri}"
                    });
                }
                else
                {
                    return NotFound(new DeleteRecordResponseDto
                    {
                        Success = false,
                        RecordUri = recordUri,
                        DeletedChunks = 0,
                        Message = $"No embeddings found for record URI {recordUri}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Failed to delete embeddings for record URI: {RecordUri}", recordUri);
                return StatusCode(500, new DeleteRecordResponseDto
                {
                    Success = false,
                    RecordUri = recordUri,
                    DeletedChunks = 0,
                    Message = $"Error deleting record embeddings: {ex.Message}"
                });
            }
        }
    }

    /// <summary>
    /// Response DTO for record processing operations
    /// </summary>
    public class ProcessRecordsResponseDto
    {
        public bool Success { get; set; }
        public int ProcessedCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response DTO for record deletion operations
    /// </summary>
    public class DeleteRecordResponseDto
    {
        public bool Success { get; set; }
        public long RecordUri { get; set; }
        public int DeletedChunks { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
