using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Entities;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace DocumentProcessingAPI.API.Controllers;

/// <summary>
/// Controller for document operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IDocumentService documentService, ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _logger = logger;
    }

    /// <summary>
    /// Upload and process a document
    /// </summary>
    /// <param name="request">Document upload request</param>
    /// <returns>Document information with processing status</returns>
    /// <response code="200">Document uploaded successfully</response>
    /// <response code="400">Invalid request or file validation failed</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(DocumentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DocumentResponseDto>> UploadDocument([FromForm] DocumentUploadRequestDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _documentService.UploadDocumentAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Document upload validation failed");
            return BadRequest(new ProblemDetails
            {
                Title = "Document Upload Failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during document upload");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while uploading the document",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Get all documents with optional filtering and pagination
    /// </summary>
    /// <param name="userId">Optional user ID filter</param>
    /// <param name="status">Optional processing status filter</param>
    /// <param name="pageNumber">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <returns>List of documents with pagination information</returns>
    /// <response code="200">Documents retrieved successfully</response>
    /// <response code="400">Invalid pagination parameters</response>
    [HttpGet]
    [ProducesResponseType(typeof(DocumentListResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DocumentListResponseDto>> GetDocuments(
        [FromQuery] string? userId = null,
        [FromQuery] ProcessingStatus? status = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 100)] int pageSize = 20)
    {
        try
        {
            var (documents, totalCount) = await _documentService.GetDocumentsAsync(userId, status, pageNumber, pageSize);

            var response = new DocumentListResponseDto
            {
                Documents = documents,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving documents");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while retrieving documents",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Get document details with chunks
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <returns>Document details with chunks</returns>
    /// <response code="200">Document details retrieved successfully</response>
    /// <response code="404">Document not found</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DocumentDetailResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDetailResponseDto>> GetDocumentDetails(Guid id)
    {
        try
        {
            var document = await _documentService.GetDocumentDetailsAsync(id);

            if (document == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Document Not Found",
                    Detail = $"Document with ID {id} was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            return Ok(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document details for ID: {DocumentId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while retrieving document details",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Delete a document and its chunks
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <returns>No content if successful</returns>
    /// <response code="204">Document deleted successfully</response>
    /// <response code="404">Document not found</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteDocument(Guid id)
    {
        try
        {
            var deleted = await _documentService.DeleteDocumentAsync(id);

            if (!deleted)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Document Not Found",
                    Detail = $"Document with ID {id} was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document: {DocumentId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while deleting the document",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Get document processing status
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <returns>Document processing status</returns>
    /// <response code="200">Status retrieved successfully</response>
    /// <response code="404">Document not found</response>
    [HttpGet("{id:guid}/status")]
    [ProducesResponseType(typeof(DocumentStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentStatusResponseDto>> GetDocumentStatus(Guid id)
    {
        try
        {
            var status = await _documentService.GetDocumentStatusAsync(id);

            if (status == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Document Not Found",
                    Detail = $"Document with ID {id} was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            return Ok(new DocumentStatusResponseDto
            {
                DocumentId = id,
                Status = status.Value,
                StatusDescription = status.Value.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document status for ID: {DocumentId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while retrieving document status",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Reprocess a failed document
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <returns>Updated document information</returns>
    /// <response code="200">Document reprocessing initiated</response>
    /// <response code="404">Document not found</response>
    [HttpPost("{id:guid}/reprocess")]
    [ProducesResponseType(typeof(DocumentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentResponseDto>> ReprocessDocument(Guid id)
    {
        try
        {
            var result = await _documentService.ReprocessDocumentAsync(id);

            if (result == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Document Not Found",
                    Detail = $"Document with ID {id} was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reprocessing document: {DocumentId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while reprocessing the document",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }
}

/// <summary>
/// Document list response with pagination
/// </summary>
public class DocumentListResponseDto
{
    public List<DocumentResponseDto> Documents { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// Document status response
/// </summary>
public class DocumentStatusResponseDto
{
    public Guid DocumentId { get; set; }
    public ProcessingStatus Status { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
}