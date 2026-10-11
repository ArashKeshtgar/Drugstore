using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PharmaFlow.Api.Auth;

/// <summary>
/// Development only: everyone is the developer pharmacist. Sign-in through ClinicHub (pharmaflow:* permissions) comes
/// later; outside development the application refuses to start without it.
/// </summary>
public class DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Dev";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "dev"), new Claim("name", "Developer pharmacist (development only)")], Scheme, "name", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

public static class UserExtensions
{
    public static string UserName(this ClaimsPrincipal user) => user.FindFirstValue("name") ?? user.Identity?.Name ?? "unknown";
}
