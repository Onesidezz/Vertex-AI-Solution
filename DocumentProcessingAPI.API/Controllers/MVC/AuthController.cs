using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessingAPI.API.Controllers.MVC;

/// <summary>
/// Authentication controller for Windows authentication with cookie persistence
/// </summary>
public class AuthController : Controller
{
    private readonly ILogger<AuthController> _logger;

    public AuthController(ILogger<AuthController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Login endpoint - triggers Windows authentication and creates cookie
    /// </summary>
    [HttpGet("/Login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        _logger.LogInformation("Login requested. ReturnUrl: {ReturnUrl}", returnUrl ?? "/");

        // Check if already authenticated with cookie
        if (User.Identity?.IsAuthenticated == true)
        {
            _logger.LogInformation("User already authenticated: {Username}", User.Identity.Name);
            return LocalRedirect(returnUrl ?? "/");
        }

        // Trigger Windows authentication (Negotiate)
        var authenticateResult = await HttpContext.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);

        if (authenticateResult.Succeeded && authenticateResult.Principal != null)
        {
            _logger.LogInformation("Windows authentication succeeded for: {Username}",
                authenticateResult.Principal.Identity?.Name);

            // Sign in with cookie to persist the authentication
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                authenticateResult.Principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });

            _logger.LogInformation("Cookie authentication created for: {Username}",
                authenticateResult.Principal.Identity?.Name);

            // Redirect to original URL or home
            return LocalRedirect(returnUrl ?? "/");
        }
        else
        {
            _logger.LogWarning("Windows authentication failed. Challenging...");

            // Challenge with Windows authentication
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = Url.Action(nameof(Login), new { returnUrl })
            }, NegotiateDefaults.AuthenticationScheme);
        }
    }

    /// <summary>
    /// Logout endpoint - clears authentication cookie
    /// </summary>
    [HttpGet("/Logout")]
    [HttpPost("/Logout")]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name ?? "Unknown";
        _logger.LogInformation("Logout requested for: {Username}", username);

        // Sign out from cookie authentication
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        _logger.LogInformation("User logged out: {Username}", username);

        return RedirectToAction("Search", "Home");
    }

    /// <summary>
    /// Access denied page
    /// </summary>
    [HttpGet("/AccessDenied")]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
