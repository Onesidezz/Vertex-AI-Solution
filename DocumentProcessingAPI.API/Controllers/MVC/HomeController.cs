using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessingAPI.API.Controllers.MVC;

/// <summary>
/// MVC Controller for Search views
/// </summary>
public class HomeController : Controller
{
    private readonly IRecordSearchService _recordSearchService;
    private readonly ContentManagerServices _contentManagerServices;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IRecordSearchService recordSearchService,
        ContentManagerServices contentManagerServices,
        ILogger<HomeController> logger)
    {
        _recordSearchService = recordSearchService;
        _contentManagerServices = contentManagerServices;
        _logger = logger;
    }

    /// <summary>
    /// Search page - Default landing page
    /// </summary>
    [HttpGet("/")]
    [HttpGet("/Search")]
    public IActionResult Search()
    {
        return View();
    }

    /// <summary>
    /// Handle search request for Content Manager records
    /// </summary>
    [HttpPost("/Search")]
    public async Task<IActionResult> Search(
        string query,
        int topK = 20,
        float minimumScore = 0.3f,
        bool useAdvancedFilter = false,
        string? uri = null,
        string? clientId = null,
        string? title = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string? contentSearch = null)
    {
        try
        {
            // Validate: Need either query or advanced filters
            if (string.IsNullOrWhiteSpace(query) && !useAdvancedFilter)
            {
                TempData["Error"] = "Please enter a search query or use advanced filters.";
                return View();
            }

            // If using advanced filters only, query can be empty/null
            if (useAdvancedFilter && string.IsNullOrWhiteSpace(query))
            {
                query = null; // Let the service handle advanced filter only mode
            }

            // Call the new Record Search API with advanced filter flag
            var result = await _recordSearchService.SearchRecordsAsync(
                query,
                metadataFilters: null,
                topK,
                minimumScore,
                useAdvancedFilter,
                uri,
                clientId,
                title,
                dateFrom,
                dateTo,
                contentSearch);

            ViewBag.Query = query;
            ViewBag.TopK = topK;
            ViewBag.MinimumScore = minimumScore;
            ViewBag.SearchResults = result;

            // Pass back advanced filter values to preserve state
            ViewBag.UseAdvancedFilter = useAdvancedFilter;
            ViewBag.AdvancedFilterUri = uri ?? "";
            ViewBag.AdvancedFilterClientId = clientId ?? "";
            ViewBag.AdvancedFilterTitle = title ?? "";
            ViewBag.AdvancedFilterDateFrom = dateFrom?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.AdvancedFilterDateTo = dateTo?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.AdvancedFilterContentSearch = contentSearch ?? "";

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
    /// Download multiple Content Manager records as a ZIP file
    /// </summary>
    [HttpPost("/DownloadMultiple")]
    public async Task<IActionResult> DownloadMultiple([FromBody] List<long> recordUris)
    {
        try
        {
            _logger.LogInformation("Bulk download requested for {Count} records", recordUris?.Count ?? 0);

            if (recordUris == null || recordUris.Count == 0)
            {
                return BadRequest(new { success = false, message = "No records selected for download" });
            }

            var fileHandler = await _contentManagerServices.DownloadMultipleAsZipAsync(recordUris);

            if (fileHandler != null && fileHandler.File != null)
            {
                _logger.LogInformation("Successfully created ZIP file: {FileName} ({Size} bytes)",
                    fileHandler.FileName, fileHandler.File.Length);

                return File(fileHandler.File, "application/zip", fileHandler.FileName);
            }

            _logger.LogWarning("Failed to create ZIP file - no electronic records found");
            return BadRequest(new { success = false, message = "No electronic records found to download" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk download");
            return StatusCode(500, new { success = false, message = $"Download failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Error page - Allow anonymous access to prevent authentication loops
    /// </summary>
    [AllowAnonymous]
    [HttpGet("/Error")]
    [HttpGet("/Home/Error")]
    public IActionResult Error()
    {
        return View();
    }
}
