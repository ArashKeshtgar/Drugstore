using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Api.Auth;
using PharmaFlow.Api.Data;
using PharmaFlow.Api.Domain;
using static PharmaFlow.Api.Endpoints.RefEndpoints;

namespace PharmaFlow.Api.Endpoints;

/// <summary>
/// Reception: a prescription arrives (paper, fax, phone, electronic), is entered drug by drug with its SIG, quantity
/// and refills, and is checked against the patient's allergies and other medicines. A drug goes to "ready to fill"
/// once it is matched to a product and a pharmacist has reviewed what the checks found.
/// </summary>
public static class PrescriptionEndpoints
{
    public record ItemBody(int? DrugId, string DrugText, string Sig, decimal Quantity, short? DaysSupply, byte RefillsAuthorized, bool NoSubstitution);
    public record ReceiveBody(int PatientId, int PrescriberId, DateOnly WrittenDate, string Channel, int? PlanId, string? Notes, ItemBody[] Items);
    public record CheckBody(int PatientId, DateOnly WrittenDate, int? PlanId, ItemBody[] Items);
    public record ReasonBody(string? Reason);
    public record MatchBody(int DrugId);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapPrescriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/prescriptions").WithTags("Reception").RequireAuthorization(Policies.Any).RequireForWrites(Policies.Reception);

        // The reception queue: what came in, newest first; by default what is still open.
        api.MapGet("", async (string? status, PharmaDbContext db) =>
        {
            var q = db.Prescriptions.AsNoTracking().AsQueryable();
            q = string.IsNullOrEmpty(status) ? q.Where(r => r.Status == RxStatus.Received || r.Status == RxStatus.InProgress || r.Status == RxStatus.OnHold) : q.Where(r => r.Status == status);
            return await q.OrderByDescending(r => r.ReceivedAt).Take(200).Select(r => new
            {
                r.PrescriptionId, r.RxNumber, r.ReceivedAt, r.Channel, r.Status, patient = r.Patient.LastName + ", " + r.Patient.FirstName, r.PatientId,
                prescriber = r.Prescriber.LastName + ", " + r.Prescriber.FirstName, plan = r.Plan != null ? r.Plan.Name : "No plan",
                items = r.Items.Count, ready = r.Items.Count(i => i.Status == ItemStatus.ReadyToFill),
                warnings = r.Items.Count(i => i.WarningsJson != "[]" && i.WarningsReviewedAt == null),
            }).ToListAsync();
        });

        api.MapGet("/{id:int}", async (int id, ClaimsPrincipal user, PharmaDbContext db, TimeProvider clock) =>
        {
            var detail = await Detail(db, id);
            if (detail is null) return Results.NotFound();
            PeopleEndpoints.Audit(db, user, clock, "View", detail.PatientId, $"Prescription {detail.RxNumber}");
            await db.SaveChangesAsync();
            return Results.Ok(detail);
        });

        // While typing: what the checks would find, and the label text, without saving.
        api.MapPost("/check", async (CheckBody b, PharmaDbContext db, TimeProvider clock) =>
        {
            if (!await db.Patients.AnyAsync(p => p.PatientId == b.PatientId)) return Field("patientId", "Choose the patient.");
            var results = await CheckAsync(db, b.PatientId, null, b.WrittenDate, b.PlanId, b.Items ?? [], Today(clock));
            return Results.Ok(results.Select(r => new { r.SigExpanded, r.DaysSupply, r.Warnings }));
        });

        api.MapPost("", async (ReceiveBody b, ClaimsPrincipal user, PharmaDbContext db, TimeProvider clock) =>
        {
            var errors = new Dictionary<string, string[]>();
            void Check(bool bad, string field, string msg) { if (bad && !errors.ContainsKey(field)) errors[field] = [msg]; }
            var today = Today(clock);
            Check(!await db.Patients.AnyAsync(p => p.PatientId == b.PatientId), "patientId", "Choose the patient.");
            Check(!await db.Prescribers.AnyAsync(p => p.PrescriberId == b.PrescriberId && p.IsActive), "prescriberId", "Choose the prescriber.");
            Check(b.WrittenDate > today, "writtenDate", "The prescription can't be dated in the future.");
            Check(b.Channel is not ("Paper" or "Fax" or "Phone" or "eRx"), "channel", "Paper, Fax, Phone or eRx.");
            Check(b.PlanId is { } pl && !await db.Plans.AnyAsync(p => p.PlanId == pl && p.IsActive), "planId", "Unknown plan.");
            Check(b.Notes?.Length > 500, "notes", "At most 500 characters.");
            Check(b.Items is null || b.Items.Length is 0 or > 20, "items", "One to 20 drugs.");
            for (var i = 0; i < (b.Items?.Length ?? 0); i++)
            {
                var it = b.Items![i];
                Check(string.IsNullOrWhiteSpace(it.DrugText) || it.DrugText.Length > 200, $"items[{i}].drugText", "What the prescriber wrote, at most 200 characters.");
                Check(string.IsNullOrWhiteSpace(it.Sig) || it.Sig.Length > 300, $"items[{i}].sig", "The directions, at most 300 characters.");
                Check(it.Quantity <= 0 || it.Quantity > 10000, $"items[{i}].quantity", "A quantity above 0.");
                Check(it.DaysSupply is < 1 or > 365, $"items[{i}].daysSupply", "1 to 365 days.");
                Check(it.RefillsAuthorized > 12, $"items[{i}].refillsAuthorized", "At most 12 refills.");
                Check(it.DrugId is { } d && !await db.Drugs.AnyAsync(x => x.DrugId == d && x.IsActive), $"items[{i}].drugId", "Unknown or inactive product.");
                // Narcotics and controlled drugs may not be refilled (part-fills are written as such).
                Check(it.DrugId is { } nd && it.RefillsAuthorized > 0 && await db.Drugs.AnyAsync(x => x.DrugId == nd && (x.ScheduleCode == "N" || x.ScheduleCode == "C")),
                    $"items[{i}].refillsAuthorized", "A narcotic or controlled drug can't have refills.");
            }
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var checks = await CheckAsync(db, b.PatientId, null, b.WrittenDate, b.PlanId, b.Items!, today);
            var now = clock.GetUtcNow().UtcDateTime;
            var rx = new Prescription
            {
                RxNumber = (await db.NextRxNumberAsync()).ToString(), PatientId = b.PatientId, PrescriberId = b.PrescriberId, WrittenDate = b.WrittenDate,
                ReceivedAt = now, Channel = b.Channel, PlanId = b.PlanId ?? await db.Patients.Where(p => p.PatientId == b.PatientId).Select(p => p.DefaultPlanId).SingleAsync(),
                Status = RxStatus.Received, Notes = Clean(b.Notes), ReceivedBy = user.UserName(), UpdatedAt = now,
                Items = b.Items!.Select((it, i) => new PrescriptionItem
                {
                    LineNumber = (byte)(i + 1), DrugId = it.DrugId, DrugText = it.DrugText.Trim(), Sig = it.Sig.Trim(), SigExpanded = checks[i].SigExpanded,
                    Quantity = it.Quantity, DaysSupply = it.DaysSupply ?? checks[i].DaysSupply, RefillsAuthorized = it.RefillsAuthorized, RefillsRemaining = it.RefillsAuthorized,
                    NoSubstitution = it.NoSubstitution, Status = ItemStatus.Received, WarningsJson = JsonSerializer.Serialize(checks[i].Warnings, Json),
                }).ToList(),
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();
            PeopleEndpoints.Audit(db, user, clock, "Create", b.PatientId, $"Prescription {rx.RxNumber} received");
            await db.SaveChangesAsync();
            return Results.Created($"/api/prescriptions/{rx.PrescriptionId}", await Detail(db, rx.PrescriptionId));
        });

        // A drug written as text gets its product; the checks run again for it.
        api.MapPost("/items/{itemId:int}/match", async (int itemId, MatchBody b, PharmaDbContext db, TimeProvider clock) =>
        {
            var (rx, item) = await ItemAsync(db, itemId);
            if (item is null) return Results.NotFound();
            if (item.Status is not (ItemStatus.Received or ItemStatus.OnHold)) return Results.Problem("This drug is past reception.", statusCode: 409);
            if (!await db.Drugs.AnyAsync(d => d.DrugId == b.DrugId && d.IsActive)) return Field("drugId", "Unknown or inactive product.");
            item.DrugId = b.DrugId;
            var check = (await CheckAsync(db, rx!.PatientId, rx.PrescriptionId, rx.WrittenDate, rx.PlanId,
                [new ItemBody(b.DrugId, item.DrugText, item.Sig, item.Quantity, item.DaysSupply, item.RefillsAuthorized, item.NoSubstitution)], Today(clock)))[0];
            item.WarningsJson = JsonSerializer.Serialize(check.Warnings, Json);
            (item.WarningsReviewedBy, item.WarningsReviewedAt) = (null, null);
            await db.SaveChangesAsync();
            return Results.Ok(await Detail(db, rx.PrescriptionId));
        });

        // The pharmacist has looked at what the checks found: the drug can be filled.
        api.MapPost("/items/{itemId:int}/review", async (int itemId, ClaimsPrincipal user, PharmaDbContext db, TimeProvider clock) =>
        {
            var (rx, item) = await ItemAsync(db, itemId);
            if (item is null) return Results.NotFound();
            if (item.DrugId is null) return Results.Problem("Choose the product first.", statusCode: 409);
            if (item.Status is not (ItemStatus.Received or ItemStatus.OnHold)) return Results.Problem("This drug is past reception.", statusCode: 409);
            item.WarningsReviewedBy = user.UserName();
            item.WarningsReviewedAt = clock.GetUtcNow().UtcDateTime;
            item.Status = ItemStatus.ReadyToFill;
            await Settle(db, rx!, clock);
            return Results.Ok(await Detail(db, rx!.PrescriptionId));
        });

        api.MapPost("/items/{itemId:int}/hold", (int itemId, ReasonBody b, PharmaDbContext db, TimeProvider clock) => SetItem(db, clock, itemId, ItemStatus.OnHold, b.Reason));
        api.MapPost("/items/{itemId:int}/cancel", (int itemId, ReasonBody b, PharmaDbContext db, TimeProvider clock) => SetItem(db, clock, itemId, ItemStatus.Cancelled, b.Reason));
    }

    public record CheckResult(string SigExpanded, short? DaysSupply, List<Warning> Warnings);

    /// <summary>The label text, days' supply and warnings for each drug, against the patient's allergies and medicines.</summary>
    public static async Task<List<CheckResult>> CheckAsync(PharmaDbContext db, int patientId, int? exceptPrescriptionId, DateOnly written, int? planId, IReadOnlyList<ItemBody> items, DateOnly today)
    {
        var codes = await db.SigCodes.AsNoTracking().Where(c => c.IsActive).ToDictionaryAsync(c => c.Code, c => (c.Expansion, c.DosesPerDay));
        var drugIds = items.Where(i => i.DrugId != null).Select(i => i.DrugId!.Value).Distinct().ToList();
        var facts = await db.Drugs.AsNoTracking().Where(d => drugIds.Contains(d.DrugId))
            .Select(d => new ReceptionChecks.DrugFacts(d.DrugId, d.BrandName, d.IngredientId, d.Ingredient.Name, d.Ingredient.AllergyClass, d.Schedule.Monitored, d.OdbLimitedUse))
            .ToDictionaryAsync(d => d.DrugId);
        var allergies = await db.Allergies.AsNoTracking().Where(a => a.PatientId == patientId).ToListAsync();
        // What the patient takes: drugs on their prescriptions of the last 120 days still open or with refills left.
        var since = today.AddDays(-120).ToDateTime(TimeOnly.MinValue);
        var taking = await db.PrescriptionItems.AsNoTracking()
            .Where(i => i.DrugId != null && i.Status != ItemStatus.Cancelled && i.PrescriptionId != exceptPrescriptionId
                && db.Prescriptions.Any(r => r.PrescriptionId == i.PrescriptionId && r.PatientId == patientId && r.ReceivedAt >= since))
            .Select(i => new ReceptionChecks.Current(i.Drug!.IngredientId, i.Drug.BrandName, db.Prescriptions.Where(r => r.PrescriptionId == i.PrescriptionId).Select(r => r.RxNumber).First()))
            .ToListAsync();
        var ingredientIds = facts.Values.Select(f => f.IngredientId).Concat(taking.Select(t => t.IngredientId)).Distinct().ToList();
        var interactions = await db.Interactions.AsNoTracking().Where(x => ingredientIds.Contains(x.IngredientAId) && ingredientIds.Contains(x.IngredientBId)).ToListAsync();
        var odb = planId is { } pid && await db.Plans.AnyAsync(p => p.PlanId == pid && p.PayerType == "ODB");
        var others = facts.Values.ToList();

        return items.Select(it =>
        {
            var sig = Sig.Expand(it.Sig ?? "", codes);
            var drug = it.DrugId is { } id && facts.TryGetValue(id, out var f) ? f : null;
            var warnings = ReceptionChecks.For(drug, it.DrugText ?? "", others, taking, allergies, interactions, written, today, odb);
            return new CheckResult(sig.Text, it.DaysSupply ?? Sig.DaysSupply(it.Quantity, sig), warnings);
        }).ToList();
    }

    public record ItemDetail(int ItemId, byte LineNumber, int? DrugId, string? Drug, string? Din, string? Ingredient, string? Schedule, string DrugText, string Sig,
        string SigExpanded, decimal Quantity, short? DaysSupply, byte RefillsAuthorized, byte RefillsRemaining, bool NoSubstitution, string Status,
        Warning[] Warnings, string? WarningsReviewedBy, DateTime? WarningsReviewedAt, string? CancelReason);
    public record RxDetail(int PrescriptionId, string RxNumber, int PatientId, string Patient, DateOnly DateOfBirth, int PrescriberId, string Prescriber,
        string PrescriberLicence, DateOnly WrittenDate, DateTime ReceivedAt, string Channel, int? PlanId, string? Plan, string Status, string? Notes, string ReceivedBy,
        object[] Allergies, ItemDetail[] Items, string? Confidentiality);

    public static async Task<RxDetail?> Detail(PharmaDbContext db, int id)
    {
        var r = await db.Prescriptions.AsNoTracking().Include(x => x.Patient).ThenInclude(p => p.Allergies).ThenInclude(a => a.Ingredient)
            .Include(x => x.Prescriber).Include(x => x.Plan).Include(x => x.Items).ThenInclude(i => i.Drug!).ThenInclude(d => d.Ingredient)
            .Include(x => x.Items).ThenInclude(i => i.Drug!).ThenInclude(d => d.Schedule)
            .SingleOrDefaultAsync(x => x.PrescriptionId == id);
        if (r is null) return null;
        return new RxDetail(r.PrescriptionId, r.RxNumber, r.PatientId, $"{r.Patient.LastName}, {r.Patient.FirstName}", r.Patient.DateOfBirth, r.PrescriberId,
            $"{r.Prescriber.LastName}, {r.Prescriber.FirstName}", $"{r.Prescriber.LicenceBody} {r.Prescriber.LicenceNumber}", r.WrittenDate, r.ReceivedAt, r.Channel,
            r.PlanId, r.Plan?.Name, r.Status, r.Notes, r.ReceivedBy,
            [.. r.Patient.Allergies.Select(a => (object)new { what = a.Ingredient?.Name ?? a.AllergyClass ?? a.Description, a.Reaction, a.Severity })],
            [.. r.Items.OrderBy(i => i.LineNumber).Select(i => new ItemDetail(i.ItemId, i.LineNumber, i.DrugId, i.Drug?.BrandName, i.Drug?.Din, i.Drug?.Ingredient.Name,
                i.Drug?.Schedule.Name, i.DrugText, i.Sig, i.SigExpanded, i.Quantity, i.DaysSupply, i.RefillsAuthorized, i.RefillsRemaining, i.NoSubstitution, i.Status,
                JsonSerializer.Deserialize<Warning[]>(i.WarningsJson, Json) ?? [], i.WarningsReviewedBy, i.WarningsReviewedAt, i.CancelReason))],
            r.Confidentiality);
    }

    private static async Task<(Prescription? Rx, PrescriptionItem? Item)> ItemAsync(PharmaDbContext db, int itemId)
    {
        var item = await db.PrescriptionItems.SingleOrDefaultAsync(i => i.ItemId == itemId);
        return item is null ? (null, null) : (await db.Prescriptions.SingleAsync(r => r.PrescriptionId == item.PrescriptionId), item);
    }

    private static async Task<IResult> SetItem(PharmaDbContext db, TimeProvider clock, int itemId, string status, string? reason)
    {
        var (rx, item) = await ItemAsync(db, itemId);
        if (item is null) return Results.NotFound();
        if (item.Status is ItemStatus.Dispensed or ItemStatus.Cancelled) return Results.Problem($"This drug is already {item.Status.ToLowerInvariant()}.", statusCode: 409);
        if (reason?.Length > 200) return Field("reason", "At most 200 characters.");
        if (status == ItemStatus.Cancelled && string.IsNullOrWhiteSpace(reason)) return Field("reason", "Say why it is cancelled.");
        item.Status = status;
        item.CancelReason = Clean(reason);
        await Settle(db, rx!, clock);
        return Results.Ok(await Detail(db, rx!.PrescriptionId));
    }

    /// <summary>The prescription's state follows its drugs.</summary>
    internal static async Task Settle(PharmaDbContext db, Prescription rx, TimeProvider clock)
    {
        await db.SaveChangesAsync();
        var statuses = await db.PrescriptionItems.Where(i => i.PrescriptionId == rx.PrescriptionId).Select(i => i.Status).ToListAsync();
        rx.Status = statuses.All(s => s == ItemStatus.Cancelled) ? RxStatus.Cancelled
            : statuses.Where(s => s != ItemStatus.Cancelled).All(s => s == ItemStatus.Dispensed) ? RxStatus.Completed
            : statuses.Any(s => s == ItemStatus.OnHold) && statuses.All(s => s is ItemStatus.OnHold or ItemStatus.Cancelled) ? RxStatus.OnHold
            : statuses.Any(s => s is ItemStatus.ReadyToFill or ItemStatus.Filled or ItemStatus.Verified or ItemStatus.Dispensed) ? RxStatus.InProgress
            : RxStatus.Received;
        rx.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
    }

    internal static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, Toronto));
}
