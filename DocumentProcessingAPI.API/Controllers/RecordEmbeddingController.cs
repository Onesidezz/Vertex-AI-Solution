using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
        //[AllowAnonymous] // Temporarily allow anonymous access for Postman testing
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

        /// <summary>
        /// Delete all embeddings (chunks) for multiple Content Manager record URIs (batch deletion)
        /// This removes all vector data associated with the records from the vector database
        /// </summary>
        /// <param name="request">Batch deletion request containing list of record URIs</param>
        /// <returns>Batch deletion results with details for each record</returns>
        [HttpPost("records/delete-batch")]
        public async Task<ActionResult<DeleteMultipleRecordsResponseDto>> DeleteMultipleRecordEmbeddings([FromBody] DeleteMultipleRecordsRequestDto request)
        {
            try
            {
                _logger.LogInformation("API: Batch deleting embeddings for {Count} record URIs", request.RecordUris?.Count ?? 0);

                if (request.RecordUris == null || !request.RecordUris.Any())
                {
                    return BadRequest(new DeleteMultipleRecordsResponseDto
                    {
                        Success = false,
                        TotalRequested = 0,
                        TotalDeleted = 0,
                        TotalNotFound = 0,
                        TotalFailed = 0,
                        Results = new List<DeleteRecordResultDto>(),
                        Message = "No record URIs provided for deletion"
                    });
                }

                var results = await _recordEmbeddingService.DeleteMultipleRecordEmbeddingsAsync(request.RecordUris);

                // Process results into detailed response
                var deletionResults = new List<DeleteRecordResultDto>();
                int totalDeleted = 0;
                int totalNotFound = 0;
                int totalFailed = 0;

                foreach (var kvp in results)
                {
                    var recordUri = kvp.Key;
                    var deletedChunks = kvp.Value;

                    if (deletedChunks > 0)
                    {
                        totalDeleted++;
                        deletionResults.Add(new DeleteRecordResultDto
                        {
                            RecordUri = recordUri,
                            DeletedChunks = deletedChunks,
                            Success = true,
                            Message = $"Successfully deleted {deletedChunks} chunks"
                        });
                    }
                    else
                    {
                        totalNotFound++;
                        deletionResults.Add(new DeleteRecordResultDto
                        {
                            RecordUri = recordUri,
                            DeletedChunks = 0,
                            Success = false,
                            Message = "No embeddings found for this record"
                        });
                    }
                }

                // Check for any URIs that weren't in the results (failed)
                foreach (var uri in request.RecordUris)
                {
                    if (!results.ContainsKey(uri))
                    {
                        totalFailed++;
                        deletionResults.Add(new DeleteRecordResultDto
                        {
                            RecordUri = uri,
                            DeletedChunks = 0,
                            Success = false,
                            Message = "Failed to process deletion"
                        });
                    }
                }

                var totalChunks = results.Values.Sum();

                return Ok(new DeleteMultipleRecordsResponseDto
                {
                    Success = totalDeleted > 0,
                    TotalRequested = request.RecordUris.Count,
                    TotalDeleted = totalDeleted,
                    TotalNotFound = totalNotFound,
                    TotalFailed = totalFailed,
                    TotalChunksDeleted = totalChunks,
                    Results = deletionResults,
                    Message = $"Batch deletion complete: {totalDeleted} records deleted ({totalChunks} chunks), {totalNotFound} not found, {totalFailed} failed"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Failed to process batch deletion");
                return StatusCode(500, new DeleteMultipleRecordsResponseDto
                {
                    Success = false,
                    TotalRequested = request.RecordUris?.Count ?? 0,
                    TotalDeleted = 0,
                    TotalNotFound = 0,
                    TotalFailed = request.RecordUris?.Count ?? 0,
                    Results = new List<DeleteRecordResultDto>(),
                    Message = $"Error processing batch deletion: {ex.Message}"
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

    /// <summary>
    /// Request DTO for batch deletion of multiple records
    /// </summary>
    public class DeleteMultipleRecordsRequestDto
    {
        public List<long> RecordUris { get; set; } = new List<long>();
    }

    /// <summary>
    /// Response DTO for batch deletion operations
    /// </summary>
    public class DeleteMultipleRecordsResponseDto
    {
        public bool Success { get; set; }
        public int TotalRequested { get; set; }
        public int TotalDeleted { get; set; }
        public int TotalNotFound { get; set; }
        public int TotalFailed { get; set; }
        public int TotalChunksDeleted { get; set; }
        public List<DeleteRecordResultDto> Results { get; set; } = new List<DeleteRecordResultDto>();
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Individual record deletion result within batch operation
    /// </summary>
    public class DeleteRecordResultDto
    {
        public long RecordUri { get; set; }
        public int DeletedChunks { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
