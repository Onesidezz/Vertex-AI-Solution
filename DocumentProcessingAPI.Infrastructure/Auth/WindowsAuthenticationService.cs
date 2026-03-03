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
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;

            // First try to get WindowsIdentity (direct Windows auth)
            if (httpContext?.User?.Identity is WindowsIdentity windowsIdentity)
            {
                _logger.LogDebug("🔍 [AUTH SERVICE] Got username from WindowsIdentity: {Username}", windowsIdentity.Name);
                return windowsIdentity.Name;
            }

            // Fallback: Get username from any authenticated identity (e.g., ClaimsIdentity from cookie)
            if (httpContext?.User?.Identity != null && httpContext.User.Identity.IsAuthenticated)
            {
                var username = httpContext.User.Identity.Name;
                _logger.LogDebug("🔍 [AUTH SERVICE] Got username from {IdentityType}: {Username}",
                    httpContext.User.Identity.GetType().Name, username);
                return username;
            }

            _logger.LogWarning("⚠️ [AUTH SERVICE] No authenticated user found");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AUTH SERVICE] Error getting username");
            return null;
        }
    }

    /// <summary>
    /// Gets the current user's display name from Active Directory
    /// </summary>
    public string? GetCurrentUserDisplayName()
    {
        var username = GetCurrentUsername();
        if (string.IsNullOrEmpty(username))
            return null;

        try
        {
            // Try domain context first
            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, username);

            return user?.DisplayName ?? username;
        }
        catch (PrincipalServerDownException)
        {
            _logger.LogDebug("Domain server unavailable, using username as display name");
            return username;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve display name from Active Directory, using username");
            return username;
        }
    }

    /// <summary>
    /// Gets the current user's email address from Active Directory
    /// </summary>
    public string? GetCurrentUserEmail()
    {
        var username = GetCurrentUsername();
        if (string.IsNullOrEmpty(username))
            return null;

        try
        {
            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, username);

            return user?.EmailAddress;
        }
        catch (PrincipalServerDownException)
        {
            _logger.LogDebug("Domain server unavailable, email not available");
            return null;
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
            var httpContext = _httpContextAccessor.HttpContext;

            // Try WindowsIdentity first (for direct Windows auth)
            if (httpContext?.User?.Identity is WindowsIdentity windowsIdentity)
            {
                var principal = new WindowsPrincipal(windowsIdentity);
                return principal.IsInRole(groupName);
            }

            // Fallback: Check claims in the current user principal
            if (httpContext?.User != null)
            {
                return httpContext.User.IsInRole(groupName);
            }

            return false;
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
        var username = GetCurrentUsername();
        if (string.IsNullOrEmpty(username))
            return Enumerable.Empty<string>();

        try
        {
            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, username);

            if (user == null)
                return Enumerable.Empty<string>();

            var groups = user.GetAuthorizationGroups()
                .Select(g => g.Name)
                .ToList();

            return groups;
        }
        catch (PrincipalServerDownException)
        {
            _logger.LogDebug("Domain server unavailable, groups not available for {Username}", username);
            return Enumerable.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting user groups for {Username}", username);
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
            var httpContext = _httpContextAccessor.HttpContext;
            var currentIdentity = httpContext?.User?.Identity;

            if (currentIdentity == null || !currentIdentity.IsAuthenticated)
                return null;

            var username = currentIdentity.Name;
            var authenticationType = currentIdentity.AuthenticationType ?? "Unknown";

            // Try to get additional info from Active Directory
            try
            {
                using var context = new PrincipalContext(ContextType.Domain);
                using var user = UserPrincipal.FindByIdentity(context, username);

                return new WindowsUserInfo
                {
                    Username = username,
                    DisplayName = user?.DisplayName ?? username,
                    EmailAddress = user?.EmailAddress,
                    IsAuthenticated = currentIdentity.IsAuthenticated,
                    AuthenticationType = authenticationType,
                    Groups = GetUserGroups().ToList()
                };
            }
            catch (PrincipalServerDownException)
            {
                _logger.LogDebug("Domain server unavailable for {Username}, returning basic info without AD data", username);

                // Return basic info if domain is unavailable
                return new WindowsUserInfo
                {
                    Username = username,
                    DisplayName = username,
                    EmailAddress = null,
                    IsAuthenticated = currentIdentity.IsAuthenticated,
                    AuthenticationType = authenticationType,
                    Groups = new List<string>()
                };
            }
            catch (Exception adEx)
            {
                _logger.LogWarning(adEx, "Could not retrieve AD info for {Username}, returning basic info", username);

                // Return basic info if AD lookup fails
                return new WindowsUserInfo
                {
                    Username = username,
                    DisplayName = username,
                    EmailAddress = null,
                    IsAuthenticated = currentIdentity.IsAuthenticated,
                    AuthenticationType = authenticationType,
                    Groups = new List<string>()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user info");
            return null;
        }
    }
}
