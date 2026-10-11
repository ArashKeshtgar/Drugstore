using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Api.Auth;
using PharmaFlow.Api.Data;
using PharmaFlow.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment() && builder.Environment.EnvironmentName != "Testing")
    throw new InvalidOperationException("PharmaFlow has only its development sign-in so far; sign-in through ClinicHub comes before production.");

builder.Services.AddDbContext<PharmaDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("PharmaFlow")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddAuthentication(DevAuthHandler.Scheme).AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.Scheme, _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => "ok").AllowAnonymous();
app.MapGet("/api/me", (System.Security.Claims.ClaimsPrincipal user) => new { name = user.UserName() }).RequireAuthorization();
app.MapRefEndpoints();
app.MapDrugEndpoints();
app.MapPeopleEndpoints();
app.MapPrescriptionEndpoints();

app.Run();

public partial class Program;
