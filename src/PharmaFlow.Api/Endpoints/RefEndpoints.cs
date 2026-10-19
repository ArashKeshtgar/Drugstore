using PharmaFlow.Api.Auth;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Api.Data;

namespace PharmaFlow.Api.Endpoints;

/// <summary>
/// Base definitions: dosage forms, routes, units, schedules (read-only: they are NAPRA's and the CDSA's), SIG codes,
/// ingredients and interactions, manufacturers, suppliers and payer plans.
/// </summary>
public static class RefEndpoints
{
    public record CodeBody(string Name, string? NameFr, bool IsActive = true);
    public record UnitBody(string Name, string Kind, bool IsActive = true);
    public record SigBody(string Expansion, string? ExpansionFr, decimal? DosesPerDay, bool IsActive = true);
    public record IngredientBody(string Name, string? AtcCode, string? AllergyClass, bool IsActive = true);
    public record InteractionBody(int IngredientAId, int IngredientBId, string Severity, string Description);
    public record ManufacturerBody(string Name, string? Country, bool IsActive = true);
    public record SupplierBody(string Code, string Name, string? Phone, string? Email, string? AccountNumber, bool IsActive = true);
    public record PlanBody(string Code, string Name, string PayerType, decimal CopayAmount, decimal CopayPercent, decimal DispensingFee, decimal MarkupPercent, string? Notes, bool IsActive = true);

    public static void MapRefEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/ref").WithTags("Base definitions").RequireAuthorization(Policies.Any).RequireForWrites(Policies.Admin);

        // Everything the forms need for their lists, in one call.
        api.MapGet("", async (PharmaDbContext db) => new
        {
            forms = await db.DosageForms.AsNoTracking().OrderBy(x => x.Name).ToListAsync(),
            routes = await db.Routes.AsNoTracking().OrderBy(x => x.Name).ToListAsync(),
            units = await db.Units.AsNoTracking().OrderBy(x => x.Kind).ThenBy(x => x.Name).ToListAsync(),
            schedules = await db.Schedules.AsNoTracking().ToListAsync(),
            sigCodes = await db.SigCodes.AsNoTracking().OrderBy(x => x.Code).ToListAsync(),
            manufacturers = await db.Manufacturers.AsNoTracking().OrderBy(x => x.Name).ToListAsync(),
            suppliers = await db.Suppliers.AsNoTracking().OrderBy(x => x.Name).ToListAsync(),
            plans = await db.Plans.AsNoTracking().OrderBy(x => x.Name).ToListAsync(),
        });

        api.MapPut("/forms/{code}", (string code, CodeBody b, PharmaDbContext db) => UpsertCode(db, db.DosageForms, code, b,
            () => new DosageForm { Code = code.ToUpperInvariant() }, (x, v) => (x.Name, x.NameFr, x.IsActive) = (v.Name.Trim(), Clean(v.NameFr), v.IsActive)));
        api.MapPut("/routes/{code}", (string code, CodeBody b, PharmaDbContext db) => UpsertCode(db, db.Routes, code, b,
            () => new RouteOfAdmin { Code = code.ToUpperInvariant() }, (x, v) => (x.Name, x.NameFr, x.IsActive) = (v.Name.Trim(), Clean(v.NameFr), v.IsActive)));
        api.MapPut("/units/{code}", async (string code, UnitBody b, PharmaDbContext db) =>
        {
            if (Bad(code, b.Name, 40) is { } bad) return bad;
            if (b.Kind is not ("Strength" or "Dispensing")) return Field("kind", "Strength or Dispensing.");
            var key = code.ToUpperInvariant();
            var x = await db.Units.FindAsync(key) ?? db.Units.Add(new Unit { Code = key }).Entity;
            (x.Name, x.Kind, x.IsActive) = (b.Name.Trim(), b.Kind, b.IsActive);
            await db.SaveChangesAsync();
            return Results.Ok(x);
        });
        api.MapPut("/sig-codes/{code}", async (string code, SigBody b, PharmaDbContext db) =>
        {
            if (Bad(code, b.Expansion, 80) is { } bad) return bad;
            if (b.DosesPerDay is <= 0 or > 48) return Field("dosesPerDay", "Between 0 and 48 a day, or empty.");
            var key = code.ToUpperInvariant();
            var x = await db.SigCodes.FindAsync(key) ?? db.SigCodes.Add(new SigCode { Code = key }).Entity;
            (x.Expansion, x.ExpansionFr, x.DosesPerDay, x.IsActive) = (b.Expansion.Trim(), Clean(b.ExpansionFr), b.DosesPerDay, b.IsActive);
            await db.SaveChangesAsync();
            return Results.Ok(x);
        });

        api.MapGet("/ingredients", async (string? q, PharmaDbContext db) =>
            await db.Ingredients.AsNoTracking().Where(i => q == null || i.Name.Contains(q) || i.AtcCode == q).OrderBy(i => i.Name).Take(300).ToListAsync());
        api.MapPost("/ingredients", (IngredientBody b, PharmaDbContext db) => SaveIngredient(db, null, b));
        api.MapPut("/ingredients/{id:int}", (int id, IngredientBody b, PharmaDbContext db) => SaveIngredient(db, id, b));

        api.MapGet("/interactions", async (int? ingredientId, PharmaDbContext db) =>
            await db.Interactions.AsNoTracking().Include(x => x.IngredientA).Include(x => x.IngredientB)
                .Where(x => ingredientId == null || x.IngredientAId == ingredientId || x.IngredientBId == ingredientId)
                .OrderBy(x => x.IngredientA.Name).ThenBy(x => x.IngredientB.Name)
                .Select(x => new { x.IngredientAId, a = x.IngredientA.Name, x.IngredientBId, b = x.IngredientB.Name, x.Severity, x.Description }).ToListAsync());
        api.MapPut("/interactions", async (InteractionBody b, PharmaDbContext db) =>
        {
            if (b.IngredientAId == b.IngredientBId) return Field("ingredientBId", "Two different ingredients.");
            if (b.Severity is not ("Major" or "Moderate" or "Minor")) return Field("severity", "Major, Moderate or Minor.");
            if (string.IsNullOrWhiteSpace(b.Description) || b.Description.Length > 400) return Field("description", "What happens, at most 400 characters.");
            var (lo, hi) = b.IngredientAId < b.IngredientBId ? (b.IngredientAId, b.IngredientBId) : (b.IngredientBId, b.IngredientAId);
            if (await db.Ingredients.CountAsync(i => i.IngredientId == lo || i.IngredientId == hi) != 2) return Field("ingredientAId", "Unknown ingredient.");
            var x = await db.Interactions.FindAsync(lo, hi) ?? db.Interactions.Add(new Interaction { IngredientAId = lo, IngredientBId = hi }).Entity;
            (x.Severity, x.Description) = (b.Severity, b.Description.Trim());
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapDelete("/interactions/{a:int}/{b:int}", async (int a, int b, PharmaDbContext db) =>
        {
            var (lo, hi) = a < b ? (a, b) : (b, a);
            if (await db.Interactions.FindAsync(lo, hi) is not { } x) return Results.NotFound();
            db.Interactions.Remove(x);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/manufacturers", (ManufacturerBody b, PharmaDbContext db) => SaveManufacturer(db, null, b));
        api.MapPut("/manufacturers/{id:int}", (int id, ManufacturerBody b, PharmaDbContext db) => SaveManufacturer(db, id, b));
        api.MapPost("/suppliers", (SupplierBody b, PharmaDbContext db) => SaveSupplier(db, null, b));
        api.MapPut("/suppliers/{id:int}", (int id, SupplierBody b, PharmaDbContext db) => SaveSupplier(db, id, b));
        api.MapPost("/plans", (PlanBody b, PharmaDbContext db) => SavePlan(db, null, b));
        api.MapPut("/plans/{id:int}", (int id, PlanBody b, PharmaDbContext db) => SavePlan(db, id, b));
    }

    private static async Task<IResult> UpsertCode<T>(PharmaDbContext db, DbSet<T> set, string code, CodeBody b, Func<T> create, Action<T, CodeBody> apply) where T : class
    {
        if (Bad(code, b.Name, 60) is { } bad) return bad;
        var x = await set.FindAsync(code.ToUpperInvariant()) ?? set.Add(create()).Entity;
        apply(x, b);
        await db.SaveChangesAsync();
        return Results.Ok(x);
    }

    private static async Task<IResult> SaveIngredient(PharmaDbContext db, int? id, IngredientBody b)
    {
        if (string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 100) return Field("name", "The generic name, at most 100 characters.");
        if (Clean(b.AtcCode) is { } atc && !System.Text.RegularExpressions.Regex.IsMatch(atc, "^[A-Z][0-9]{2}[A-Z]{0,2}[0-9]{0,2}$")) return Field("atcCode", "A WHO ATC code, like N02BE01.");
        if (await db.Ingredients.AnyAsync(i => i.Name == b.Name.Trim() && i.IngredientId != id)) return Field("name", "Already in the list.");
        var x = id is null ? db.Ingredients.Add(new Ingredient()).Entity : await db.Ingredients.FindAsync(id.Value);
        if (x is null) return Results.NotFound();
        (x.Name, x.AtcCode, x.AllergyClass, x.IsActive) = (b.Name.Trim(), Clean(b.AtcCode), Clean(b.AllergyClass), b.IsActive);
        await db.SaveChangesAsync();
        return Results.Ok(x);
    }

    private static async Task<IResult> SaveManufacturer(PharmaDbContext db, int? id, ManufacturerBody b)
    {
        if (string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 120) return Field("name", "A name, at most 120 characters.");
        if (await db.Manufacturers.AnyAsync(m => m.Name == b.Name.Trim() && m.ManufacturerId != id)) return Field("name", "Already in the list.");
        var x = id is null ? db.Manufacturers.Add(new Manufacturer()).Entity : await db.Manufacturers.FindAsync(id.Value);
        if (x is null) return Results.NotFound();
        (x.Name, x.Country, x.IsActive) = (b.Name.Trim(), Clean(b.Country)?.ToUpperInvariant(), b.IsActive);
        await db.SaveChangesAsync();
        return Results.Ok(x);
    }

    private static async Task<IResult> SaveSupplier(PharmaDbContext db, int? id, SupplierBody b)
    {
        if (string.IsNullOrWhiteSpace(b.Code) || b.Code.Length > 20) return Field("code", "A short code, at most 20 characters.");
        if (string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 120) return Field("name", "A name, at most 120 characters.");
        var code = b.Code.Trim().ToUpperInvariant();
        if (await db.Suppliers.AnyAsync(s => s.Code == code && s.SupplierId != id)) return Field("code", "This code is taken.");
        var x = id is null ? db.Suppliers.Add(new Supplier()).Entity : await db.Suppliers.FindAsync(id.Value);
        if (x is null) return Results.NotFound();
        (x.Code, x.Name, x.Phone, x.Email, x.AccountNumber, x.IsActive) = (code, b.Name.Trim(), Clean(b.Phone), Clean(b.Email), Clean(b.AccountNumber), b.IsActive);
        await db.SaveChangesAsync();
        return Results.Ok(x);
    }

    private static async Task<IResult> SavePlan(PharmaDbContext db, int? id, PlanBody b)
    {
        if (string.IsNullOrWhiteSpace(b.Code) || b.Code.Length > 20) return Field("code", "A short code, at most 20 characters.");
        if (string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 120) return Field("name", "A name, at most 120 characters.");
        if (b.PayerType is not ("ODB" or "Insurer" or "Cash" or "Other")) return Field("payerType", "ODB, Insurer, Cash or Other.");
        if (b.CopayAmount < 0 || b.DispensingFee < 0 || b.CopayPercent is < 0 or > 100 || b.MarkupPercent is < 0 or > 100)
            return Field("amounts", "Amounts are not negative; percentages are 0 to 100.");
        var code = b.Code.Trim().ToUpperInvariant();
        if (await db.Plans.AnyAsync(p => p.Code == code && p.PlanId != id)) return Field("code", "This code is taken.");
        var x = id is null ? db.Plans.Add(new Plan()).Entity : await db.Plans.FindAsync(id.Value);
        if (x is null) return Results.NotFound();
        (x.Code, x.Name, x.PayerType, x.CopayAmount, x.CopayPercent, x.DispensingFee, x.MarkupPercent, x.Notes, x.IsActive) =
            (code, b.Name.Trim(), b.PayerType, b.CopayAmount, b.CopayPercent, b.DispensingFee, b.MarkupPercent, Clean(b.Notes), b.IsActive);
        await db.SaveChangesAsync();
        return Results.Ok(x);
    }

    private static IResult? Bad(string code, string? name, int max) =>
        string.IsNullOrWhiteSpace(code) || code.Length > 12 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? Field("code", "Letters, digits or _, at most 12.")
        : string.IsNullOrWhiteSpace(name) || name.Length > max ? Field("name", $"Required, at most {max} characters.")
        : null;

    public static IResult Field(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
