using PharmaFlow.Api.Auth;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Api.Data;
using static PharmaFlow.Api.Endpoints.RefEndpoints;

namespace PharmaFlow.Api.Endpoints;

/// <summary>The drug catalog: one row per DIN.</summary>
public static class DrugEndpoints
{
    public record DrugRow(int DrugId, string Din, string BrandName, int IngredientId, string Ingredient, decimal? Strength, string? StrengthUnit,
        string FormCode, string RouteCode, string ScheduleCode, string Schedule, bool Monitored, int? ManufacturerId, string? Manufacturer,
        int PackSize, string DispensingUnit, decimal UnitCost, bool OdbBenefit, bool OdbLimitedUse, string? InterchangeGroup, bool IsActive, bool IsDemo, string Form, string? StrengthUnitName);
    public record DrugBody(string Din, string BrandName, int IngredientId, decimal? Strength, string? StrengthUnit, string FormCode, string RouteCode,
        string ScheduleCode, int? ManufacturerId, int PackSize, string DispensingUnit, decimal UnitCost, bool OdbBenefit, bool OdbLimitedUse,
        string? InterchangeGroup, bool IsActive = true);

    public static void MapDrugEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/drugs").WithTags("Drugs").RequireAuthorization(Policies.Any).RequireForWrites(Policies.Admin);

        // Finds by DIN, brand or ingredient; the ingredient's other products come along (interchangeable choices).
        api.MapGet("", async (string? q, bool? all, PharmaDbContext db) =>
        {
            var term = (q ?? "").Trim();
            var query = db.Drugs.AsNoTracking().Where(d => all == true || d.IsActive);
            if (term.Length > 0)
                query = query.Where(d => d.Din.StartsWith(term) || d.BrandName.Contains(term) || d.Ingredient.Name.Contains(term));
            return await Rows(db, query.OrderBy(d => d.Ingredient.Name).ThenBy(d => d.Strength).ThenBy(d => d.BrandName).Take(200)).ToListAsync();
        });
        api.MapGet("/{id:int}", async (int id, PharmaDbContext db) => await Rows(db, db.Drugs.Where(d => d.DrugId == id)).SingleOrDefaultAsync() is { } d ? Results.Ok(d) : Results.NotFound());
        api.MapPost("", (DrugBody b, PharmaDbContext db, TimeProvider clock) => Save(db, clock, null, b));
        api.MapPut("/{id:int}", (int id, DrugBody b, PharmaDbContext db, TimeProvider clock) => Save(db, clock, id, b));
    }

    public static IQueryable<DrugRow> Rows(PharmaDbContext db, IQueryable<Drug> source) => source.AsNoTracking().Select(d => new DrugRow(
        d.DrugId, d.Din, d.BrandName, d.IngredientId, d.Ingredient.Name, d.Strength, d.StrengthUnit, d.FormCode, d.RouteCode, d.ScheduleCode,
        d.Schedule.Name, d.Schedule.Monitored, d.ManufacturerId, d.Manufacturer != null ? d.Manufacturer.Name : null, d.PackSize, d.DispensingUnit,
        d.UnitCost, d.OdbBenefit, d.OdbLimitedUse, d.InterchangeGroup, d.IsActive, d.IsDemo,
        db.DosageForms.Where(f => f.Code == d.FormCode).Select(f => f.Name).FirstOrDefault() ?? d.FormCode,
        db.Units.Where(u => u.Code == d.StrengthUnit).Select(u => u.Name).FirstOrDefault()));

    private static async Task<IResult> Save(PharmaDbContext db, TimeProvider clock, int? id, DrugBody b)
    {
        var din = (b.Din ?? "").Trim();
        var errors = new Dictionary<string, string[]>();
        void Check(bool bad, string field, string msg) { if (bad && !errors.ContainsKey(field)) errors[field] = [msg]; }
        Check(din.Length != 8 || !din.All(char.IsAsciiDigit), "din", "A DIN is 8 digits.");
        Check(await db.Drugs.AnyAsync(d => d.Din == din && d.DrugId != id), "din", "This DIN is already in the catalog.");
        Check(string.IsNullOrWhiteSpace(b.BrandName) || b.BrandName.Length > 100, "brandName", "The product name, at most 100 characters.");
        Check(!await db.Ingredients.AnyAsync(i => i.IngredientId == b.IngredientId), "ingredientId", "Choose the ingredient.");
        Check((b.Strength is null) != (Clean(b.StrengthUnit) is null) || b.Strength <= 0, "strength", "A strength with its unit, or neither.");
        Check(Clean(b.StrengthUnit) is { } su && !await db.Units.AnyAsync(u => u.Code == su && u.Kind == "Strength"), "strengthUnit", "Unknown strength unit.");
        Check(!await db.DosageForms.AnyAsync(f => f.Code == b.FormCode), "formCode", "Choose the dosage form.");
        Check(!await db.Routes.AnyAsync(r => r.Code == b.RouteCode), "routeCode", "Choose the route.");
        Check(!await db.Schedules.AnyAsync(s => s.Code == b.ScheduleCode), "scheduleCode", "Choose the schedule.");
        Check(b.ManufacturerId is { } m && !await db.Manufacturers.AnyAsync(x => x.ManufacturerId == m), "manufacturerId", "Unknown manufacturer.");
        Check(!await db.Units.AnyAsync(u => u.Code == b.DispensingUnit && u.Kind == "Dispensing"), "dispensingUnit", "Choose the dispensing unit.");
        Check(b.PackSize <= 0, "packSize", "At least 1.");
        Check(b.UnitCost < 0 || decimal.Round(b.UnitCost, 4) != b.UnitCost, "unitCost", "A cost per unit, up to 4 decimals.");
        Check(b.InterchangeGroup?.Length > 20, "interchangeGroup", "At most 20 characters.");
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = clock.GetUtcNow().UtcDateTime;
        var d = id is null ? db.Drugs.Add(new Drug { CreatedAt = now }).Entity : await db.Drugs.FindAsync(id.Value);
        if (d is null) return Results.NotFound();
        (d.Din, d.BrandName, d.IngredientId, d.Strength, d.StrengthUnit, d.FormCode, d.RouteCode, d.ScheduleCode, d.ManufacturerId, d.PackSize,
            d.DispensingUnit, d.UnitCost, d.OdbBenefit, d.OdbLimitedUse, d.InterchangeGroup, d.IsActive, d.UpdatedAt) =
            (din, b.BrandName.Trim(), b.IngredientId, b.Strength, Clean(b.StrengthUnit), b.FormCode, b.RouteCode, b.ScheduleCode, b.ManufacturerId, b.PackSize,
             b.DispensingUnit, b.UnitCost, b.OdbBenefit, b.OdbLimitedUse, Clean(b.InterchangeGroup)?.ToUpperInvariant(), b.IsActive, now);
        await db.SaveChangesAsync();
        return Results.Ok(await Rows(db, db.Drugs.Where(x => x.DrugId == d.DrugId)).SingleAsync());
    }
}
