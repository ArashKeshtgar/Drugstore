using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace PharmaFlow.Api.Auth;

/// <summary>
/// What ClinicHub's roles can allow in PharmaFlow (its "pharmaflow:*" keys, which reach PharmaFlow without the prefix).
/// Administration implies the others, so a clinic can build a role from this one alone.
/// </summary>
public static class PharmaPermissions
{
    public const string ClaimType = "permission";

    /// <summary>Receive and review prescriptions; patients, allergies and prescribers.</summary>
    public const string Reception = "reception";

    /// <summary>Fill, verify and dispense.</summary>
    public const string Dispense = "dispense";

    /// <summary>Purchasing and stock.</summary>
    public const string Stock = "stock";

    /// <summary>The drug catalog, plans and base definitions.</summary>
    public const string Admin = "admin";

    public static readonly string[] All = [Reception, Dispense, Stock, Admin];
}

public static class Policies
{
    /// <summary>Anyone who may use PharmaFlow at all: reads of what the pharmacy works from.</summary>
    public const string Any = "pharmacy";
    public const string Reception = "pharmacy.reception";
    public const string Dispense = "pharmacy.dispense";
    public const string Stock = "pharmacy.stock";
    public const string Admin = "pharmacy.admin";

    public static AuthorizationBuilder AddPharmaPolicies(this AuthorizationBuilder auth) => auth
        .AddPolicy(Any, p => p.RequireAuthenticatedUser().RequireClaim(PharmaPermissions.ClaimType, PharmaPermissions.All))
        .AddPolicy(Reception, p => p.RequireAuthenticatedUser().RequireClaim(PharmaPermissions.ClaimType, PharmaPermissions.Reception, PharmaPermissions.Admin))
        .AddPolicy(Dispense, p => p.RequireAuthenticatedUser().RequireClaim(PharmaPermissions.ClaimType, PharmaPermissions.Dispense, PharmaPermissions.Admin))
        .AddPolicy(Stock, p => p.RequireAuthenticatedUser().RequireClaim(PharmaPermissions.ClaimType, PharmaPermissions.Stock, PharmaPermissions.Admin))
        .AddPolicy(Admin, p => p.RequireAuthenticatedUser().RequireClaim(PharmaPermissions.ClaimType, PharmaPermissions.Admin));

    /// <summary>Reads need only <see cref="Any"/> (the group's own requirement); anything that changes data needs this policy too.</summary>
    public static RouteGroupBuilder RequireForWrites(this RouteGroupBuilder group, string policy) => group.AddEndpointFilter(async (ctx, next) =>
    {
        var method = ctx.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return await next(ctx);
        var result = await ctx.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(ctx.HttpContext.User, policy);
        return result.Succeeded ? await next(ctx) : Results.Problem("Your role at the clinic does not allow this in PharmaFlow.", statusCode: StatusCodes.Status403Forbidden);
    });

    public static string[] PermissionsOf(this ClaimsPrincipal user) => [.. user.FindAll(PharmaPermissions.ClaimType).Select(c => c.Value)];
}
