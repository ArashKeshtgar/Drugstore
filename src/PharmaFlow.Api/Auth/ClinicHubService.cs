using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace PharmaFlow.Api.Auth;

/// <summary>
/// ClinicHub as PharmaFlow's source of electronic prescriptions (https://github.com/ArashKeshtgar/ClinicHub). Without
/// <see cref="Authority"/> there is no /fhir endpoint.
/// </summary>
public class ClinicHubOptions
{
    /// <summary>ClinicHub's address, which is the issuer of its tokens, e.g. http://localhost:5301/. Empty: not connected.</summary>
    public string? Authority { get; set; }

    /// <summary>Where the keys for ClinicHub's incoming prescriptions are fetched when that is not {Authority}.well-known/openid-configuration
    /// (development: the API directly, so prescriptions don't depend on the web proxy). Sign-in always uses the Authority's own document.</summary>
    public string? MetadataAddress { get; set; }

    /// <summary>The application's client id in ClinicHub, which is also the audience of the tokens it sends.</summary>
    public string ClientId { get; set; } = "pharmaflow";

    /// <summary>The application's secret in ClinicHub. With it, staff sign in through ClinicHub; without it, only the development sign-in exists.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>How often a signed-in session is checked with ClinicHub again, and how long ClinicHub may be unreachable before it ends.</summary>
    public int RecheckMinutes { get; set; } = 5;
    public int OfflineGraceMinutes { get; set; } = 30;

    public bool RequireHttpsMetadata { get; set; } = true;
    public bool Enabled => !string.IsNullOrWhiteSpace(Authority);
    public bool SignInEnabled => Enabled && !string.IsNullOrWhiteSpace(ClientSecret);
}

public static class ClinicHubService
{
    /// <summary>ClinicHub calling PharmaFlow itself: a bearer token signed by ClinicHub, for this audience.</summary>
    public const string Scheme = "ClinicHubService";
    public const string PrescriptionsPolicy = "clinichub.prescriptions";
    public const string ClinicClaim = "clinic_slug";
    public const string ClinicNameClaim = "clinic_name";

    public static AuthenticationBuilder AddClinicHubService(this AuthenticationBuilder auth, IConfiguration config)
    {
        var hub = config.GetSection("ClinicHub").Get<ClinicHubOptions>() ?? new ClinicHubOptions();
        auth.Services.Configure<ClinicHubOptions>(config.GetSection("ClinicHub"));
        if (!hub.Enabled)
            return auth;
        auth.AddJwtBearer(Scheme, o =>
        {
            o.Authority = hub.Authority;
            if (!string.IsNullOrWhiteSpace(hub.MetadataAddress))
                o.MetadataAddress = hub.MetadataAddress;
            o.Audience = hub.ClientId;
            o.RequireHttpsMetadata = hub.RequireHttpsMetadata;
            o.MapInboundClaims = false;
        });
        auth.Services.AddAuthorizationBuilder().AddPolicy(PrescriptionsPolicy, p => p
            .AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser()
            .RequireClaim("scope", "prescriptions")
            .RequireAssertion(ctx => IsValidSlug(ctx.User.FindFirstValue(ClinicClaim))));
        return auth;
    }

    public static bool IsValidSlug(string? slug) =>
        slug is { Length: >= 2 and <= 40 } && slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
