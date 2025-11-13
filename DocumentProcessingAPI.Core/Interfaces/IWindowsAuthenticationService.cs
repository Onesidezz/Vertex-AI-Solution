using DocumentProcessingAPI.Core.DTOs;
using System.Security.Principal;

namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Interface for Windows authentication service
/// </summary>
public interface IWindowsAuthenticationService
{
    /// <summary>
    /// Gets the current Windows user identity
    /// </summary>
    WindowsIdentity? GetCurrentWindowsIdentity();

    /// <summary>
    /// Gets the current authenticated Windows username
    /// </summary>
    string? GetCurrentUsername();

    /// <summary>
    /// Gets the current user's display name from Active Directory
    /// </summary>
    string? GetCurrentUserDisplayName();

    /// <summary>
    /// Gets the current user's email address from Active Directory
    /// </summary>
    string? GetCurrentUserEmail();

    /// <summary>
    /// Checks if the current user is in a specific Windows group
    /// </summary>
    bool IsUserInGroup(string groupName);

    /// <summary>
    /// Gets all groups the current user belongs to
    /// </summary>
    IEnumerable<string> GetUserGroups();

    /// <summary>
    /// Gets detailed information about the current user
    /// </summary>
    WindowsUserInfo? GetCurrentUserInfo();
}
