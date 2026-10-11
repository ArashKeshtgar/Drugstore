using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Api.Auth;
using PharmaFlow.Api.Data;
using PharmaFlow.Api.Domain;
using static PharmaFlow.Api.Endpoints.RefEndpoints;

namespace PharmaFlow.Api.Endpoints;

/// <summary>Patients (with their allergies and drug plan) and prescribers.</summary>
public static class PeopleEndpoints
{
    public record PatientBody(string? HealthCardNumber, string? HealthCardVersion, string FirstName, string LastName, DateOnly? DateOfBirth, string? Sex,
        string? Phone, string? Email, string? AddressLine1, string? City, string? PostalCode, int? DefaultPlanId, string? PlanMemberId, string? Notes, string? RowVersion);
    public record AllergyBody(int? IngredientId, string? AllergyClass, string? Description, string? Reaction, string? Severity);
    public record PrescriberBody(string LicenceBody, string LicenceNumber, string FirstName, string LastName, string? Phone, string? Fax, string? Clinic, bool IsActive = true);

    public static void MapPeopleEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/patients", async (string? q, PharmaDbContext db) =>
        {
            var term = (q ?? "").Trim();
            if (term.Length < 2) return Results.Ok(Array.Empty<object>());
            var digits = new string(term.Where(char.IsDigit).ToArray());
            var dob = DateOnly.TryParseExact(term, "yyyy-MM-dd", out var d) ? d : (DateOnly?)null;
            return Results.Ok(await db.Patients.AsNoTracking()
                .Where(p => p.LastName.StartsWith(term) || (p.LastName + ", " + p.FirstName).StartsWith(term) || (digits.Length == 10 && p.HealthCardNumber == digits)
                    || p.DateOfBirth == dob || (digits.Length >= 7 && p.Phone != null && p.Phone.Replace("(", "").Replace(")", "").Replace("-", "").Replace(" ", "").EndsWith(digits)))
                .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Take(30)
                .Select(p => new { p.PatientId, name = p.LastName + ", " + p.FirstName, p.DateOfBirth, p.Sex, p.Phone, allergies = p.Allergies.Count })
                .ToListAsync());
        }).WithTags("Patients");

        api.MapGet("/patients/{id:int}", async (int id, ClaimsPrincipal user, PharmaDbContext db, TimeProvider clock) =>
        {
            var p = await db.Patients.AsNoTracking().Include(x => x.Allergies).ThenInclude(a => a.Ingredient).SingleOrDefaultAsync(x => x.PatientId == id);
            if (p is null) return Results.NotFound();
            Audit(db, user, clock, "View", id, "Patient profile");
            await db.SaveChangesAsync();
            var recent = await db.Prescriptions.AsNoTracking().Where(r => r.PatientId == id).OrderByDescending(r => r.ReceivedAt).Take(20)
                .Select(r => new { r.PrescriptionId, r.RxNumber, r.ReceivedAt, r.Status, items = r.Items.Count }).ToListAsync();
            return Results.Ok(new
            {
                patient = new
                {
                    p.PatientId, p.HealthCardNumber, p.HealthCardVersion, p.FirstName, p.LastName, p.DateOfBirth, p.Sex, p.Phone, p.Email,
                    p.AddressLine1, p.City, p.PostalCode, p.DefaultPlanId, p.PlanMemberId, p.Notes, rowVersion = Convert.ToBase64String(p.RowVer),
                },
                allergies = p.Allergies.OrderBy(a => a.AllergyId).Select(a => new { a.AllergyId, a.IngredientId, ingredient = a.Ingredient?.Name, a.AllergyClass, a.Description, a.Reaction, a.Severity }),
                prescriptions = recent,
            });
        }).WithTags("Patients");

        api.MapPost("/patients", (PatientBody b, ClaimsPrincipal user, PharmaDbContext db, TimeProvider clock) => SavePatient(db, user, clock, null, b)).WithTags("Patients");
        api.MapPut("/patients/{id:int}", (int id, PatientBody b, ClaimsPrincipal user, PharmaDbContext db, TimeProvider clock) => SavePatient(db, user, clock, id, b)).WithTags("Patients");

        api.MapPost("/patients/{id:int}/allergies", async (int id, AllergyBody b, ClaimsPrincipal user, PharmaDbContext db, TimeProvider clock) =>
        {
            if (!await db.Patients.AnyAsync(p => p.PatientId == id)) return Results.NotFound();
            if (b.IngredientId is null && Clean(b.AllergyClass) is null && Clean(b.Description) is null)
                return Field("description", "Name the ingredient, the family, or what the patient said.");
            if (b.IngredientId is { } i && !await db.Ingredients.AnyAsync(x => x.IngredientId == i)) return Field("ingredientId", "Unknown ingredient.");
            if (b.Severity is not (null or "Mild" or "Moderate" or "Severe" or "Unknown")) return Field("severity", "Mild, Moderate, Severe or Unknown.");
            db.Allergies.Add(new PatientAllergy
            {
                PatientId = id, IngredientId = b.IngredientId, AllergyClass = Clean(b.AllergyClass), Description = Clean(b.Description),
                Reaction = Clean(b.Reaction), Severity = b.Severity ?? "Unknown", RecordedAt = clock.GetUtcNow().UtcDateTime,
            });
            Audit(db, user, clock, "Update", id, "Allergy recorded");
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).WithTags("Patients");
        api.MapDelete("/allergies/{id:int}", async (int id, ClaimsPrincipal user, PharmaDbContext db, TimeProvider clock) =>
        {
            if (await db.Allergies.FindAsync(id) is not { } a) return Results.NotFound();
            db.Allergies.Remove(a);
            Audit(db, user, clock, "Update", a.PatientId, "Allergy removed");
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).WithTags("Patients");

        api.MapGet("/prescribers", async (string? q, PharmaDbContext db) =>
        {
            var term = (q ?? "").Trim();
            return await db.Prescribers.AsNoTracking()
                .Where(p => term == "" || p.LastName.StartsWith(term) || p.LicenceNumber == term || (p.LastName + ", " + p.FirstName).StartsWith(term))
                .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Take(50).ToListAsync();
        }).WithTags("Prescribers");
        api.MapPost("/prescribers", (PrescriberBody b, PharmaDbContext db) => SavePrescriber(db, null, b)).WithTags("Prescribers");
        api.MapPut("/prescribers/{id:int}", (int id, PrescriberBody b, PharmaDbContext db) => SavePrescriber(db, id, b)).WithTags("Prescribers");
    }

    private static async Task<IResult> SavePatient(PharmaDbContext db, ClaimsPrincipal user, TimeProvider clock, int? id, PatientBody b)
    {
        var errors = new Dictionary<string, string[]>();
        void Check(bool bad, string field, string msg) { if (bad && !errors.ContainsKey(field)) errors[field] = [msg]; }
        var card = Clean(b.HealthCardNumber) is { } c ? HealthCard.Normalize(c) : null;
        Check(string.IsNullOrWhiteSpace(b.FirstName) || b.FirstName.Length > 60, "firstName", "Required, at most 60 characters.");
        Check(string.IsNullOrWhiteSpace(b.LastName) || b.LastName.Length > 60, "lastName", "Required, at most 60 characters.");
        Check(b.DateOfBirth is null || b.DateOfBirth > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) || b.DateOfBirth < new DateOnly(1900, 1, 1), "dateOfBirth", "Check the date of birth.");
        Check(b.Sex is not (null or "M" or "F" or "X" or "U"), "sex", "M, F, X or U.");
        Check(card is not null && !HealthCard.IsValidNumber(card), "healthCardNumber", "Not a valid Ontario health card number.");
        Check(card is not null && await db.Patients.AnyAsync(p => p.HealthCardNumber == card && p.PatientId != id), "healthCardNumber", "Another patient has this health card.");
        Check(b.DefaultPlanId is { } pl && !await db.Plans.AnyAsync(x => x.PlanId == pl), "defaultPlanId", "Unknown plan.");
        Check(Clean(b.PostalCode) is { } pc && !System.Text.RegularExpressions.Regex.IsMatch(pc.ToUpperInvariant(), @"^[A-Z]\d[A-Z] ?\d[A-Z]\d$"), "postalCode", "Like M5V 0A1.");
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = clock.GetUtcNow().UtcDateTime;
        var p = id is null ? db.Patients.Add(new Patient { CreatedAt = now }).Entity : await db.Patients.FindAsync(id.Value);
        if (p is null) return Results.NotFound();
        if (id is not null && b.RowVersion is { } rv)
            db.Entry(p).Property(x => x.RowVer).OriginalValue = Convert.FromBase64String(rv);
        (p.HealthCardNumber, p.HealthCardVersion, p.FirstName, p.LastName, p.DateOfBirth, p.Sex, p.Phone, p.Email, p.AddressLine1, p.City, p.PostalCode,
            p.DefaultPlanId, p.PlanMemberId, p.Notes, p.UpdatedAt) =
            (card, card is null ? null : Clean(b.HealthCardVersion)?.ToUpperInvariant(), b.FirstName.Trim(), b.LastName.Trim(), b.DateOfBirth!.Value, b.Sex ?? "U",
             Clean(b.Phone), Clean(b.Email), Clean(b.AddressLine1), Clean(b.City), Clean(b.PostalCode)?.ToUpperInvariant(), b.DefaultPlanId, Clean(b.PlanMemberId), Clean(b.Notes), now);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem("Someone else just changed this patient. Reload and try again.", statusCode: StatusCodes.Status409Conflict);
        }
        Audit(db, user, clock, id is null ? "Create" : "Update", p.PatientId, id is null ? "Patient registered" : "Patient updated");
        await db.SaveChangesAsync();
        return Results.Ok(new { p.PatientId });
    }

    private static async Task<IResult> SavePrescriber(PharmaDbContext db, int? id, PrescriberBody b)
    {
        if (b.LicenceBody is not ("CPSO" or "CNO" or "RCDSO" or "OCP")) return Field("licenceBody", "CPSO, CNO, RCDSO or OCP.");
        var number = (b.LicenceNumber ?? "").Trim();
        if (number.Length is < 4 or > 10 || !number.All(char.IsAsciiLetterOrDigit)) return Field("licenceNumber", "The registration number, 4 to 10 characters.");
        if (string.IsNullOrWhiteSpace(b.FirstName) || string.IsNullOrWhiteSpace(b.LastName)) return Field("lastName", "First and last name.");
        if (await db.Prescribers.AnyAsync(p => p.LicenceBody == b.LicenceBody && p.LicenceNumber == number && p.PrescriberId != id)) return Field("licenceNumber", "Already registered.");
        var p = id is null ? db.Prescribers.Add(new Prescriber()).Entity : await db.Prescribers.FindAsync(id.Value);
        if (p is null) return Results.NotFound();
        (p.LicenceBody, p.LicenceNumber, p.FirstName, p.LastName, p.Phone, p.Fax, p.Clinic, p.IsActive) =
            (b.LicenceBody, number, b.FirstName.Trim(), b.LastName.Trim(), Clean(b.Phone), Clean(b.Fax), Clean(b.Clinic), b.IsActive);
        await db.SaveChangesAsync();
        return Results.Ok(p);
    }

    public static void Audit(PharmaDbContext db, ClaimsPrincipal user, TimeProvider clock, string action, int? patientId, string context) =>
        db.AccessLog.Add(new AccessLog { OccurredAt = clock.GetUtcNow().UtcDateTime, UserName = user.UserName(), Action = action, PatientId = patientId, Context = context });
}
