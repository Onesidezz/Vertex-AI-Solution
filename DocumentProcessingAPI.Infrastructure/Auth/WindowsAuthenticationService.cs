using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.DirectoryServices.AccountManagement;
using System.Security.Principal;

namespace DocumentProcessingAPI.Infrastructure.Auth;

/// <summary>
/// Service for handling Windows authentication and user information
/// </summary>
public class WindowsAuthenticationService : IWindowsAuthenticationService
{
    private readonly ILogger<WindowsAuthenticationService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WindowsAuthenticationService(
        ILogger<WindowsAuthenticationService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current Windows user identity
    /// </summary>
    public WindowsIdentity? GetCurrentWindowsIdentity()
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity is WindowsIdentity windowsIdentity)
            {
                _logger.LogDebug("🔍 [AUTH SERVICE] Windows identity found: {Username}, IsAuthenticated: {IsAuth}",
                    windowsIdentity.Name, windowsIdentity.IsAuthenticated);
                return windowsIdentity;
            }

            _logger.LogWarning("⚠️ [AUTH SERVICE] No Windows identity found in current HTTP context");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AUTH SERVICE] Error getting Windows identity");
            return null;
        }
    }

    /// <summary>
    /// Gets the current authenticated Windows username
    /// </summary>
    public string? GetCurrentUsername()
    {
        var identity = GetCurrentWindowsIdentity();
        return identity?.Name;
    }

    /// <summary>
    /// Gets the current user's display name from Active Directory
    /// </summary>
    public string? GetCurrentUserDisplayName()
    {
        try
        {
            var identity = GetCurrentWindowsIdentity();
            if (identity == null)
                return null;

            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, identity.Name);

            return user?.DisplayName ?? identity.Name;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve display name from Active Directory, using identity name");
            return GetCurrentUsername();
        }
    }

    /// <summary>
    /// Gets the current user's email address from Active Directory
    /// </summary>
    public string? GetCurrentUserEmail()
    {
        try
        {
            var identity = GetCurrentWindowsIdentity();
            if (identity == null)
                return null;

            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, identity.Name);

            return user?.EmailAddress;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve email from Active Directory");
            return null;
        }
    }

    /// <summary>
    /// Checks if the current user is in a specific Windows group
    /// </summary>
    public bool IsUserInGroup(string groupName)
    {
        try
        {
            var identity = GetCurrentWindowsIdentity();
            if (identity == null)
                return false;

            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(groupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking group membership for {GroupName}", groupName);
            return false;
        }
    }

    /// <summary>
    /// Gets all groups the current user belongs to
    /// </summary>
    public IEnumerable<string> GetUserGroups()
    {
        try
        {
            var identity = GetCurrentWindowsIdentity();
            if (identity == null)
                return Enumerable.Empty<string>();

            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, identity.Name);

            if (user == null)
                return Enumerable.Empty<string>();

            var groups = user.GetAuthorizationGroups()
                .Select(g => g.Name)
                .ToList();

            return groups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user groups");
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// Gets detailed information about the current user
    /// </summary>
    public WindowsUserInfo? GetCurrentUserInfo()
    {
        try
        {
            var identity = GetCurrentWindowsIdentity();
            if (identity == null)
                return null;

            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, identity.Name);

            return new WindowsUserInfo
            {
                Username = identity.Name,
                DisplayName = user?.DisplayName ?? identity.Name,
                EmailAddress = user?.EmailAddress,
                IsAuthenticated = identity.IsAuthenticated,
                AuthenticationType = identity.AuthenticationType,
                Groups = GetUserGroups().ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user info");

            // Return basic info if AD lookup fails
            var identity = GetCurrentWindowsIdentity();
            if (identity != null)
            {
                return new WindowsUserInfo
                {
                    Username = identity.Name,
                    DisplayName = identity.Name,
                    IsAuthenticated = identity.IsAuthenticated,
                    AuthenticationType = identity.AuthenticationType,
                    Groups = new List<string>()
                };
            }

            return null;
        }
    }
}
