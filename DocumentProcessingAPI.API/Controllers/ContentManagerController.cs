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
    /// <summary>
    /// View file inline in browser (new endpoint for viewing)
    /// </summary>
    [HttpGet("view/{recordUri}")]
    public async Task<IActionResult> ViewFile(long recordUri)
    {
        try
        {
            _logger.LogInformation("Viewing file inline for record URI: {RecordUri}", recordUri);

            var fileHandler = await _contentManagerServices.DownloadAsync(recordUri);

            if (fileHandler == null || fileHandler.File == null)
            {
                return NotFound("File not found");
            }

            var contentType = GetContentType(fileHandler.FileName);

            // Check if browser can display this file type inline
            if (CanDisplayInline(fileHandler.FileName))
            {
                // Return file for inline display - NO Content-Disposition header
                return File(fileHandler.File, contentType);
            }
            else
            {
                // Force download for file types that browsers can't display
                return File(fileHandler.File, contentType, fileHandler.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error viewing file for record URI: {RecordUri}", recordUri);
            return StatusCode(500, "Error loading file");
        }
    }

    [HttpGet("download/{recordUri}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadFile(long recordUri, [FromQuery] bool inline = false)
    {
        try
        {
            _logger.LogInformation("Downloading file for record URI: {RecordUri}, Inline: {Inline}", recordUri, inline);

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

            // Determine content type based on file extension
            var contentType = GetContentType(fileHandler.FileName);

            // If inline is requested, return with proper content type
            if (inline)
            {
                // IMPORTANT: Don't manually set Content-Disposition - let ASP.NET Core handle it
                // Using File() with 2 parameters automatically sets Content-Disposition: inline
                // Add Content-Length header for Chrome compatibility
                Response.Headers.Append("Content-Length", fileHandler.File.Length.ToString());

                // Return file with content type only - NO filename parameter
                // This prevents download and allows inline display
                return File(fileHandler.File, contentType);
            }
            else
            {
                // Force download with attachment disposition
                return File(fileHandler.File, "application/octet-stream", fileHandler.FileName);
            }
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

    /// <summary>
    /// Get content type based on file extension
    /// </summary>
    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            // Documents
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".xml" => "text/xml",
            ".json" => "application/json",
            ".css" => "text/css",
            ".js" => "application/javascript",

            // Images
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",

            // Office Documents
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",

            // Video
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",

            // Audio
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",

            // Archives
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",

            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Check if a file type can be displayed inline by modern browsers
    /// </summary>
    private static bool CanDisplayInline(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            // Files that browsers can display inline
            ".pdf" => true,
            ".jpg" or ".jpeg" => true,
            ".png" => true,
            ".gif" => true,
            ".svg" => true,
            ".webp" => true,
            ".txt" => true,
            ".html" or ".htm" => true,
            ".xml" => true,
            ".json" => true,
            ".css" => true,
            ".js" => true,

            // Files that should be downloaded (browsers cannot display inline)
            ".doc" or ".docx" => false,
            ".xls" or ".xlsx" => false,
            ".ppt" or ".pptx" => false,
            ".zip" or ".rar" or ".7z" => false,
            ".exe" or ".msi" => false,
            ".mp4" or ".avi" or ".mov" => true,  // Video files can play inline
            ".mp3" or ".wav" => true,  // Audio files can play inline

            // Default: force download for unknown types
            _ => false
        };
    }
}