using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Principal;

namespace DocumentProcessingAPI.API.Middleware;

/// <summary>
/// Middleware to log authentication details for every request
/// </summary>
public class AuthenticationLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationLoggingMiddleware> _logger;

    public AuthenticationLoggingMiddleware(RequestDelegate next, ILogger<AuthenticationLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Log request details
        var path = context.Request.Path;
        var method = context.Request.Method;

        // Log authentication information
        if (context.User?.Identity != null)
        {
            var identity = context.User.Identity;
            var isAuthenticated = identity.IsAuthenticated;
            var authenticationType = identity.AuthenticationType ?? "None";
            var name = identity.Name ?? "Anonymous";

            if (isAuthenticated)
            {
                _logger.LogInformation(
                    "🔐 [AUTH] {Method} {Path} | User: {Username} | Auth Type: {AuthType} | Authenticated: {IsAuth}",
                    method, path, name, authenticationType, isAuthenticated);

                // If Windows Identity, log additional details
                if (identity is WindowsIdentity windowsIdentity)
                {
                    _logger.LogInformation(
                        "🪟 [WINDOWS AUTH] User: {Username} | Impersonation Level: {ImpersonationLevel}",
                        windowsIdentity.Name,
                        windowsIdentity.ImpersonationLevel);
                }
            }
            else
            {
                _logger.LogWarning(
                    "⚠️ [NO AUTH] {Method} {Path} | User: {Username} | Auth Type: {AuthType} | Authenticated: {IsAuth}",
                    method, path, name, authenticationType, isAuthenticated);
            }
        }
        else
        {
            _logger.LogWarning("⚠️ [NO IDENTITY] {Method} {Path} | No user identity found", method, path);
        }

        // Continue to next middleware
        await _next(context);

        // Log response status
        _logger.LogInformation(
            "✅ [RESPONSE] {Method} {Path} | Status: {StatusCode}",
            method, path, context.Response.StatusCode);
    }
}

/// <summary>
/// Extension method to add the authentication logging middleware
/// </summary>
public static class AuthenticationLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthenticationLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthenticationLoggingMiddleware>();
    }
}
