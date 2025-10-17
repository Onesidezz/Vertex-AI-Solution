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
}