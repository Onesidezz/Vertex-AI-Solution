using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessingAPI.API.Controllers.MVC;

/// <summary>
/// MVC Controller for Upload and Search views
/// </summary>
public class HomeController : Controller
{
    private readonly IDocumentService _documentService;
    private readonly IRecordSearchService _recordSearchService;
    private readonly ContentManagerServices _contentManagerServices;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IDocumentService documentService,
        IRecordSearchService recordSearchService,
        ContentManagerServices contentManagerServices,
        ILogger<HomeController> logger)
    {
        _documentService = documentService;
        _recordSearchService = recordSearchService;
        _contentManagerServices = contentManagerServices;
        _logger = logger;
    }

    /// <summary>
    /// Upload page - Default landing page
    /// </summary>
    [HttpGet("/")]
    [HttpGet("/Upload")]
    public IActionResult Upload()
    {
        return View();
    }

    /// <summary>
    /// Handle document upload
    /// </summary>
    [HttpPost("/Upload")]
    [RequestSizeLimit(100_000_000)] // 100 MB limit
    public async Task<IActionResult> Upload(IFormFile file, string userId = "")
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return View();
            }

            var request = new DocumentUploadRequestDto
            {
                File = file,
                UserId = userId,
                ProcessImmediately = true
            };

            var result = await _documentService.UploadDocumentAsync(request);

            TempData["Success"] = $"Document '{result.FileName}' uploaded successfully! " +
                                 $"Status: {result.Status}, Total Chunks: {result.TotalChunks}";
            TempData["DocumentId"] = result.Id.ToString();

            return RedirectToAction("Upload");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document");
            TempData["Error"] = $"Upload failed: {ex.Message}";
            return View();
        }
    }

    /// <summary>
    /// Search page
    /// </summary>
    [HttpGet("/Search")]
    public IActionResult Search()
    {
        return View();
    }

    /// <summary>
    /// Handle search request for Content Manager records
    /// </summary>
    [HttpPost("/Search")]
    public async Task<IActionResult> Search(string query, int topK = 20, float minimumScore = 0.3f)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                TempData["Error"] = "Please enter a search query.";
                return View();
            }

            // Call the new Record Search API
            var result = await _recordSearchService.SearchRecordsAsync(
                query,
                metadataFilters: null,
                topK,
                minimumScore);

            ViewBag.Query = query;
            ViewBag.TopK = topK;
            ViewBag.MinimumScore = minimumScore;
            ViewBag.SearchResults = result;

            return View();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search");
            TempData["Error"] = $"Search failed: {ex.Message}";
            return View();
        }
    }

    /// <summary>
    /// Download a Content Manager record file by URI
    /// </summary>
    [HttpGet("/Download/{recordUri}")]
    public async Task<IActionResult> Download(long recordUri)
    {
        try
        {
            _logger.LogInformation("Downloading record URI: {RecordUri}", recordUri);

            var fileHandler = await _contentManagerServices.DownloadAsync(recordUri);

            if (fileHandler != null && fileHandler.File != null)
            {
                _logger.LogInformation("Successfully downloaded file: {FileName} ({Size} bytes)",
                    fileHandler.FileName, fileHandler.File.Length);

                // Return file with proper content type and filename
                return File(fileHandler.File, "application/octet-stream", fileHandler.FileName);
            }

            _logger.LogWarning("Record URI {RecordUri} not found or has no electronic document", recordUri);
            TempData["Error"] = $"Record {recordUri} not found or has no downloadable file.";
            return RedirectToAction("Search");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading record URI: {RecordUri}", recordUri);
            TempData["Error"] = $"Download failed: {ex.Message}";
            return RedirectToAction("Search");
        }
    }

    /// <summary>
    /// Error page
    /// </summary>
    [HttpGet("/Error")]
    public IActionResult Error()
    {
        return View();
    }
}
