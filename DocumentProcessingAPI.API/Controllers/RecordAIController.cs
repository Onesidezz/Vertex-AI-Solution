using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessingAPI.API.Controllers
{
    /// <summary>
    /// Controller for AI-powered record operations (Summary and Q&A)
    /// Provides intelligent analysis using Gemini AI
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class RecordAIController : ControllerBase
    {
        private readonly IAIRecordService _aiRecordService;
        private readonly ILogger<RecordAIController> _logger;

        public RecordAIController(
            IAIRecordService aiRecordService,
            ILogger<RecordAIController> logger)
        {
            _aiRecordService = aiRecordService;
            _logger = logger;
        }

        /// <summary>
        /// Get AI-generated summary of a record
        /// </summary>
        /// <param name="recordUri">Content Manager record URI</param>
        /// <returns>AI-generated summary</returns>
        [HttpGet("summary/{recordUri}")]
        public async Task<IActionResult> GetSummary([FromRoute] long recordUri)
        {
            try
            {
                _logger.LogInformation("Generating summary for record URI: {RecordUri}", recordUri);

                var summary = await _aiRecordService.GetRecordSummaryAsync(recordUri);

                return Ok(new
                {
                    RecordUri = recordUri,
                    Summary = summary,
                    GeneratedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating summary for record URI: {RecordUri}", recordUri);
                return StatusCode(500, new
                {
                    Error = "Failed to generate summary",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Ask a question about a specific record using AI
        /// </summary>
        /// <param name="recordUri">Content Manager record URI</param>
        /// <param name="request">Question request</param>
        /// <returns>AI-generated answer</returns>
        [HttpPost("ask/{recordUri}")]
        public async Task<IActionResult> AskQuestion(
            [FromRoute] long recordUri,
            [FromBody] AskQuestionRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Question))
                {
                    return BadRequest(new { Error = "Question is required" });
                }

                _logger.LogInformation("Processing AI question for record URI: {RecordUri}", recordUri);

                var answer = await _aiRecordService.AskAboutRecordAsync(recordUri, request.Question);

                return Ok(new
                {
                    RecordUri = recordUri,
                    Question = request.Question,
                    Answer = answer,
                    GeneratedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AI question for record URI: {RecordUri}", recordUri);
                return StatusCode(500, new
                {
                    Error = "Failed to process question",
                    Message = ex.Message
                });
            }
        }
    }

    /// <summary>
    /// Request model for Ask AI endpoint
    /// </summary>
    public class AskQuestionRequest
    {
        /// <summary>
        /// User's question about the record
        /// </summary>
        public string Question { get; set; } = string.Empty;
    }
}
