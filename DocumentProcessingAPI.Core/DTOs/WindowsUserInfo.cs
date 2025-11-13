namespace DocumentProcessingAPI.Core.DTOs;

/// <summary>
/// Information about a Windows authenticated user
/// </summary>
public class WindowsUserInfo
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? EmailAddress { get; set; }
    public bool IsAuthenticated { get; set; }
    public string? AuthenticationType { get; set; }
    public List<string> Groups { get; set; } = new();
}
