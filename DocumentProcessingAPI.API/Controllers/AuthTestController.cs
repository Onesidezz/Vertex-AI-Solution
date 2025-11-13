using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessingAPI.API.Controllers;

/// <summary>
/// Test controller to verify Windows Authentication is working
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthTestController : ControllerBase
{
    private readonly IWindowsAuthenticationService _authService;
    private readonly ILogger<AuthTestController> _logger;

    public AuthTestController(
        IWindowsAuthenticationService authService,
        ILogger<AuthTestController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Test endpoint - Shows current authentication status (requires authentication)
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetAuthStatus()
    {
        var isEnabled = HttpContext.User?.Identity?.IsAuthenticated ?? false;
        var authType = HttpContext.User?.Identity?.AuthenticationType ?? "None";

        _logger.LogInformation("Auth status check - IsAuthenticated: {IsAuthenticated}, AuthType: {AuthType}",isEnabled, authType);

        return Ok(new
        {
            Message = "Auth Test Controller is running",
            IsAuthenticated = isEnabled,
            AuthenticationType = authType,
            Timestamp = DateTime.Now
        });
    }

    /// <summary>
    /// Get current Windows user information (requires authentication)
    /// </summary>
    [HttpGet("whoami")]
    [Authorize]
    public IActionResult WhoAmI()
    {
        try
        {
            var userInfo = _authService.GetCurrentUserInfo();

            if (userInfo == null)
            {
                return Ok(new
                {
                    Message = "Windows Authentication is enabled but no user info found",
                    IsAuthenticated = HttpContext.User?.Identity?.IsAuthenticated ?? false
                });
            }

            _logger.LogInformation("User {Username} accessed /whoami endpoint", userInfo.Username);

            return Ok(new
            {
                Message = "Windows Authentication is working!",
                User = userInfo,
                RequestInfo = new
                {
                    RemoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    RequestPath = HttpContext.Request.Path.Value,
                    Timestamp = DateTime.Now
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user info");
            return StatusCode(500, new
            {
                Error = "Failed to get user information",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Get just the username (requires authentication)
    /// </summary>
    [HttpGet("username")]
    [Authorize]
    public IActionResult GetUsername()
    {
        var username = _authService.GetCurrentUsername();
        var displayName = _authService.GetCurrentUserDisplayName();
        var email = _authService.GetCurrentUserEmail();

        _logger.LogInformation("Username endpoint accessed by: {Username}, Display: {DisplayName}, Email: {Email}",
            username, displayName, email);

        return Ok(new
        {
            Username = username,
            DisplayName = displayName,
            Email = email
        });
    }

    /// <summary>
    /// Get user's Windows groups (requires authentication)
    /// </summary>
    [HttpGet("groups")]
    [Authorize]
    public IActionResult GetUserGroups()
    {
        try
        {
            var username = _authService.GetCurrentUsername();
            var groups = _authService.GetUserGroups().ToList();

            _logger.LogInformation("User {Username} groups retrieved. Count: {GroupCount}",
                username, groups.Count);

            return Ok(new
            {
                Username = username,
                GroupCount = groups.Count,
                Groups = groups
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user groups for {Username}",
                _authService.GetCurrentUsername());
            return Ok(new
            {
                Error = "Could not retrieve groups (may not be on domain)",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Check if user is in a specific group (requires authentication)
    /// </summary>
    [HttpGet("check-group/{groupName}")]
    [Authorize]
    public IActionResult CheckGroup(string groupName)
    {
        var username = _authService.GetCurrentUsername();
        var isMember = _authService.IsUserInGroup(groupName);

        _logger.LogInformation("User {Username} checked membership in group {GroupName}. IsMember: {IsMember}",
            username, groupName, isMember);

        return Ok(new
        {
            Username = username,
            Group = groupName,
            IsMember = isMember
        });
    }

    /// <summary>
    /// Test endpoint that allows anonymous access
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    public IActionResult PublicEndpoint()
    {
        _logger.LogInformation("Public endpoint accessed - no authentication required");

        return Ok(new
        {
            Message = "This is a public endpoint - no authentication required",
            Timestamp = DateTime.Now
        });
    }

    /// <summary>
    /// Debug endpoint - shows auth headers and status (requires authentication)
    /// </summary>
    [HttpGet("debug")]
    public IActionResult DebugAuth()
    {
        var headers = Request.Headers
            .Where(h => h.Key.Contains("Auth", StringComparison.OrdinalIgnoreCase) ||
                       h.Key.Contains("WWW", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key, h => h.Value.ToString());

        var identity = HttpContext.User?.Identity;
        var isWindowsIdentity = identity is System.Security.Principal.WindowsIdentity;

        _logger.LogInformation("Debug endpoint accessed - IsAuth: {IsAuth}, IsWindows: {IsWindows}",
            identity?.IsAuthenticated, isWindowsIdentity);

        return Ok(new
        {
            IsAuthenticated = identity?.IsAuthenticated ?? false,
            AuthenticationType = identity?.AuthenticationType ?? "None",
            Username = identity?.Name ?? "Anonymous",
            IsWindowsIdentity = isWindowsIdentity,
            Headers = headers,
            Timestamp = DateTime.Now
        });
    }
}
