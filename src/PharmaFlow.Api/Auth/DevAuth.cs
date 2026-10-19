using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PharmaFlow.Api.Auth;

/// <summary>
/// Development only: everyone is the developer pharmacist. With ClinicHub:ClientSecret set, staff sign in through ClinicHub
/// instead (ClinicHubSignIn); outside development the application refuses to start without that.
/// </summary>
public class DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Dev";
    public const string PermissionsHeader = "X-Dev-Permissions";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim> { new("sub", "dev"), new("name", "Developer pharmacist (development only)") };
        // Everything, unless X-Dev-Permissions narrows it ("reception stock"; "none" for nothing): to try a role without ClinicHub.
        var granted = Request.Headers.TryGetValue(PermissionsHeader, out var asked)
            ? PharmaPermissions.All.Where(p => asked.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(p))
            : PharmaPermissions.All;
        claims.AddRange(granted.Select(p => new Claim(PharmaPermissions.ClaimType, p)));
        var identity = new ClaimsIdentity(claims, Scheme, "name", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

public static class UserExtensions
{
    public static string UserName(this ClaimsPrincipal user) => user.FindFirstValue("name") ?? user.Identity?.Name ?? "unknown";
}
