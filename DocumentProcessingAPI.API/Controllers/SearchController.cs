using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace DocumentProcessingAPI.API.Controllers;

/// <summary>
/// Controller for semantic search operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;
    private readonly IRagService _ragService;
    private readonly ILogger<SearchController> _logger;

    public SearchController(ISearchService searchService, IRagService ragService, ILogger<SearchController> logger)
    {
        _searchService = searchService;
        _ragService = ragService;
        _logger = logger;
    }

    /// <summary>
    /// Perform semantic search across all documents
    /// </summary>
    /// <param name="request">Search request parameters</param>
    /// <returns>Search results with relevance scores</returns>
    /// <response code="200">Search completed successfully</response>
    /// <response code="400">Invalid search request</response>
    /// <response code="500">Internal server error</response>
    [HttpPost]
    [ProducesResponseType(typeof(SearchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SearchResponseDto>> Search([FromBody] SearchRequestDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var results = await _searchService.SearchAsync(request);
            return Ok(results);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid search request: {Query}", request?.Query);
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Search Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search: {Query}", request?.Query);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while performing the search",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Search within a specific document
    /// </summary>
    /// <param name="documentId">Document ID to search within</param>
    /// <param name="request">Search request parameters</param>
    /// <returns>Search results from the specific document</returns>
    /// <response code="200">Search completed successfully</response>
    /// <response code="400">Invalid search request</response>
    /// <response code="404">Document not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("document/{documentId:guid}")]
    [ProducesResponseType(typeof(SearchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SearchResponseDto>> SearchDocument(
        Guid documentId,
        [FromBody] SearchRequestDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var results = await _searchService.SearchDocumentAsync(documentId, request);

            if (results.TotalResults == 0 && !string.IsNullOrEmpty(results.Query))
            {
                // Check if document exists by attempting a basic query
                // This is a simple check - in production you might want a dedicated document existence check
                return Ok(results); // Return empty results rather than 404 - document might exist but have no matches
            }

            return Ok(results);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid document search request: Document {DocumentId}, Query {Query}",
                documentId, request?.Query);
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Search Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing document search: Document {DocumentId}, Query {Query}",
                documentId, request?.Query);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while performing the document search",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Get chunks similar to a specific chunk
    /// </summary>
    /// <param name="chunkId">Reference chunk ID</param>
    /// <param name="topK">Number of similar chunks to return (1-50)</param>
    /// <returns>Similar chunks with relevance scores</returns>
    /// <response code="200">Similar chunks retrieved successfully</response>
    /// <response code="400">Invalid parameters</response>
    /// <response code="404">Chunk not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("similar/{chunkId:guid}")]
    [ProducesResponseType(typeof(SimilarChunksResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SimilarChunksResponseDto>> GetSimilarChunks(
        Guid chunkId,
        [FromQuery][Range(1, 50)] int topK = 5)
    {
        try
        {
            var similarChunks = await _searchService.GetSimilarChunksAsync(chunkId, topK);

            if (!similarChunks.Any())
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Chunk Not Found",
                    Detail = $"Chunk with ID {chunkId} was not found or has no similar chunks",
                    Status = StatusCodes.Status404NotFound
                });
            }

            var response = new SimilarChunksResponseDto
            {
                ReferenceChunkId = chunkId,
                SimilarChunks = similarChunks,
                Count = similarChunks.Count
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar chunks for: {ChunkId}", chunkId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while finding similar chunks",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Export search results to CSV format
    /// </summary>
    /// <param name="request">Search request parameters</param>
    /// <returns>CSV file with search results</returns>
    /// <response code="200">CSV export successful</response>
    /// <response code="400">Invalid search request</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("export/csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> ExportSearchResultsToCsv([FromBody] SearchRequestDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var csvData = await _searchService.ExportSearchResultsToCsvAsync(request);

            var fileName = $"search_results_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

            return File(csvData, "text/csv", fileName);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid CSV export request: {Query}", request?.Query);
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Export Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting search results to CSV: {Query}", request?.Query);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while exporting search results",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Get search suggestions based on partial query
    /// </summary>
    /// <param name="partialQuery">Partial query string</param>
    /// <param name="limit">Number of suggestions to return</param>
    /// <returns>List of query suggestions</returns>
    /// <response code="200">Suggestions retrieved successfully</response>
    /// <response code="400">Invalid request</response>
    [HttpGet("suggestions")]
    [ProducesResponseType(typeof(SearchSuggestionsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchSuggestionsResponseDto>> GetSearchSuggestions(
        [FromQuery][Required] string partialQuery,
        [FromQuery][Range(1, 20)] int limit = 5)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(partialQuery) || partialQuery.Length < 2)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Query",
                    Detail = "Partial query must be at least 2 characters long",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // For now, return simple suggestions based on the partial query
            // In a production system, you might maintain a search history or use more sophisticated methods
            var suggestions = GenerateSimpleSuggestions(partialQuery, limit);

            var response = new SearchSuggestionsResponseDto
            {
                PartialQuery = partialQuery,
                Suggestions = suggestions
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating search suggestions for: {PartialQuery}", partialQuery);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while generating suggestions",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    private static List<string> GenerateSimpleSuggestions(string partialQuery, int limit)
    {
        // Simple suggestion logic - in production, you might use a more sophisticated approach
        var commonTerms = new[]
        {
            "document", "file", "content", "text", "data", "information", "report", "analysis",
            "summary", "details", "overview", "description", "specification", "requirements",
            "process", "procedure", "workflow", "guidelines", "instructions", "manual"
        };

        var suggestions = commonTerms
            .Where(term => term.StartsWith(partialQuery, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();

        // Add the original query if not already present
        if (!suggestions.Contains(partialQuery, StringComparer.OrdinalIgnoreCase))
        {
            suggestions.Insert(0, partialQuery);
            suggestions = suggestions.Take(limit).ToList();
        }

        return suggestions;
    }

    /// <summary>
    /// Ask a natural language question and get an AI-generated response with sources
    /// </summary>
    /// <param name="request">The RAG request containing the question and parameters</param>
    /// <returns>AI response with source citations</returns>
    /// <response code="200">Question answered successfully</response>
    /// <response code="400">Invalid request</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("rag/ask")]
    [ProducesResponseType(typeof(RagResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RagResponseDto>> AskQuestion([FromBody] RagRequestDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Processing RAG question: {Question}", request.Question);

            var response = await _ragService.AskQuestionAsync(request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid RAG request: {Question}", request.Question);
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RAG question: {Question}", request.Question);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while processing your question.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Ask a question with streaming response
    /// </summary>
    /// <param name="request">The RAG request</param>
    /// <returns>Streaming AI response</returns>
    /// <response code="200">Question processing started</response>
    /// <response code="400">Invalid request</response>
    [HttpPost("rag/ask/stream")]
    [ProducesResponseType(typeof(RagStreamResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AskQuestionStream([FromBody] RagRequestDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Processing streaming RAG question: {Question}", request.Question);

            Response.Headers.Add("Content-Type", "application/json");
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("Connection", "keep-alive");

            await foreach (var chunk in _ragService.AskQuestionStreamAsync(request))
            {
                await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(chunk)}\n\n");
                await Response.Body.FlushAsync();
            }

            return new EmptyResult();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid streaming RAG request: {Question}", request.Question);
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in streaming RAG: {Question}", request.Question);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Ask a question about a specific document
    /// </summary>
    /// <param name="documentId">Document ID to search within</param>
    /// <param name="request">The RAG request</param>
    /// <returns>AI response with document-specific sources</returns>
    /// <response code="200">Question answered successfully</response>
    /// <response code="400">Invalid request</response>
    /// <response code="404">Document not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("rag/ask/document/{documentId:guid}")]
    [ProducesResponseType(typeof(RagResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RagResponseDto>> AskQuestionAboutDocument(
        [FromRoute] Guid documentId,
        [FromBody] RagRequestDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Processing document-specific RAG question for document {DocumentId}: {Question}",
                documentId, request.Question);

            var response = await _ragService.AskQuestionAboutDocumentAsync(documentId, request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid document-specific RAG request: {DocumentId}, {Question}",
                documentId, request.Question);
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Document not found: {DocumentId}", documentId);
            return NotFound(new ProblemDetails
            {
                Title = "Document Not Found",
                Detail = $"Document with ID {documentId} was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing document-specific RAG question: {DocumentId}, {Question}",
                documentId, request.Question);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while processing your question.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }
}

/// <summary>
/// Similar chunks response
/// </summary>
public class SimilarChunksResponseDto
{
    public Guid ReferenceChunkId { get; set; }
    public List<SearchResultDto> SimilarChunks { get; set; } = new();
    public int Count { get; set; }
}

/// <summary>
/// Search suggestions response
/// </summary>
public class SearchSuggestionsResponseDto
{
    public string PartialQuery { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = new();
}