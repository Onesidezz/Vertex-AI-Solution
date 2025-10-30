using DocumentProcessingAPI.Core.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessingAPI.API.Controllers;

/// <summary>
/// Controller for Content Manager operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ContentManagerController : ControllerBase
{
    private readonly ContentManagerServices _contentManagerServices;
    private readonly ILogger<ContentManagerController> _logger;

    public ContentManagerController(
        ContentManagerServices contentManagerServices,
        ILogger<ContentManagerController> logger)
    {
        _contentManagerServices = contentManagerServices;
        _logger = logger;
    }

    /// <summary>
    /// Get records from Content Manager
    /// </summary>
    /// <param name="search">Search string for filtering records. Use "*" to get all records.</param>
    /// <returns>List of records from Content Manager</returns>
    /// <response code="200">Records retrieved successfully</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("records")]
    [ProducesResponseType(typeof(List<RecordViewModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<RecordViewModel>>> GetRecords([FromQuery] string search = "*")
    {
        try
        {
            var records = await _contentManagerServices.GetRecordsAsync(search);
            return Ok(records);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving records with search: {SearchString}", search);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while retrieving records",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Download a file from Content Manager by record URI
    /// </summary>
    /// <param name="recordUri">The Content Manager record URI (unique identifier)</param>
    /// <returns>File download stream</returns>
    /// <response code="200">File downloaded successfully</response>
    /// <response code="404">Record not found or has no downloadable file</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("download/{recordUri}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadFile(long recordUri)
    {
        try
        {
            _logger.LogInformation("Downloading file for record URI: {RecordUri}", recordUri);

            var fileHandler = await _contentManagerServices.DownloadAsync(recordUri);

            if (fileHandler == null || fileHandler.File == null)
            {
                _logger.LogWarning("Record {RecordUri} not found or has no downloadable file", recordUri);
                return NotFound(new ProblemDetails
                {
                    Title = "File Not Found",
                    Detail = $"Record {recordUri} not found or has no downloadable file",
                    Status = StatusCodes.Status404NotFound
                });
            }

            _logger.LogInformation("Successfully retrieved file: {FileName} ({Size} bytes)",
                fileHandler.FileName, fileHandler.File.Length);

            // Return file with proper content type and filename
            return File(fileHandler.File, "application/octet-stream", fileHandler.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file for record URI: {RecordUri}", recordUri);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while downloading the file",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }
}