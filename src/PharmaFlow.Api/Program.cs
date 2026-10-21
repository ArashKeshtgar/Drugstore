using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Api.Auth;
using PharmaFlow.Api.Data;
using PharmaFlow.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Staff sign in through ClinicHub when it is configured (ClinicHub:Authority and ClientSecret); otherwise there is only the
// development stub, which exists in Development and in tests and nowhere else.
var hub = builder.Configuration.GetSection("ClinicHub").Get<ClinicHubOptions>() ?? new ClinicHubOptions();
var signInThroughClinicHub = hub.SignInEnabled;
if (!signInThroughClinicHub && !builder.Environment.IsDevelopment() && builder.Environment.EnvironmentName != "Testing")
    throw new InvalidOperationException("Set ClinicHub:Authority and ClinicHub:ClientSecret: outside development staff sign in through ClinicHub.");

builder.Services.AddDbContext<PharmaDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("PharmaFlow")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
var authentication = builder.Services.AddAuthentication(signInThroughClinicHub ? Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme : DevAuthHandler.Scheme);
if (signInThroughClinicHub)
    authentication.AddClinicHubSignIn(hub);
else
    authentication.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.Scheme, _ => { });
authentication.AddClinicHubService(builder.Configuration);
builder.Services.AddAuthorizationBuilder().AddPharmaPolicies();
// The pharmacy's money to FinancePowerApp, when Finance:BaseUrl is set.
builder.Services.Configure<PharmaFlow.Api.Domain.FinanceOptions>(builder.Configuration.GetSection("Finance"));
builder.Services.AddHttpClient(PharmaFlow.Api.Domain.FinanceFeed.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<PharmaFlow.Api.Domain.FinanceFeed>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PharmaFlow.Api.Auth.ConfidentialityGate>();
builder.Services.AddHostedService<PharmaFlow.Api.Domain.FinanceWorker>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.UseAuthentication();
app.UseAuthorization();
if (signInThroughClinicHub)
    app.UseCsrfHeaderCheck();

app.MapGet("/healthz", () => "ok").AllowAnonymous();
// Who is signed in (nobody: name is null), for the React app.
app.MapGet("/api/me", (System.Security.Claims.ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
    ? Results.Ok(new { name = user.UserName(), permissions = user.PermissionsOf(), clinic = user.FindFirst(ClinicHubSignIn.ClinicName)?.Value })
    : Results.Ok(new { name = (string?)null, permissions = Array.Empty<string>(), clinic = (string?)null })).AllowAnonymous();
// How people sign in: "clinichub", or the development stub.
app.MapGet("/api/auth/settings", () => new { signIn = signInThroughClinicHub ? "clinichub" : "demo" }).AllowAnonymous();
if (signInThroughClinicHub)
{
    app.MapGet("/api/auth/sso/start", (string? returnUrl) => Results.Challenge(
        new AuthenticationProperties { RedirectUri = returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/" },
        [ClinicHubSignIn.Scheme])).AllowAnonymous();
    // One sign-out: here and at ClinicHub.
    app.MapGet("/api/auth/sso/signout", () => Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
        [Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme, ClinicHubSignIn.Scheme])).AllowAnonymous();
}
app.MapRefEndpoints();
app.MapDrugEndpoints();
app.MapPeopleEndpoints();
app.MapPrescriptionEndpoints();
app.MapInventoryEndpoints();
app.MapFhirEndpoints();

app.Run();

public partial class Program;
