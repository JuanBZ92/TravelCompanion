using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;

namespace TravelCompanion.Api.Pages;

public sealed class LoginModel(IOptions<AdminAuthOptions> authOptions) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var options = authOptions.Value;
        if (!IsMatch(Input.Username, options.Username) || !IsMatch(Input.Password, options.Password))
        {
            ErrorMessage = "Usuario o password incorrecto.";
            return Page();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, options.Username),
            new Claim(ClaimTypes.Role, "Admin")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        return LocalRedirect(IsLocalReturnUrl(ReturnUrl) ? ReturnUrl! : "/admin");
    }

    private static bool IsMatch(string? actual, string expected)
    {
        var actualBytes = Encoding.UTF8.GetBytes(actual ?? string.Empty);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static bool IsLocalReturnUrl(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl)
            && Uri.TryCreate(returnUrl, UriKind.Relative, out _)
            && returnUrl.StartsWith('/');
    }

    public sealed class LoginInput
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
