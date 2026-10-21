using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmaFlow.Api.Auth;
using PharmaFlow.Api.Data;
using static PharmaFlow.Api.Endpoints.PrescriptionEndpoints;

namespace PharmaFlow.Api.Endpoints;

/// <summary>
/// Electronic prescriptions from ClinicHub (docs of IntegrationHub, contracts flow 5), as one FHIR R4 transaction Bundle
/// to /fhir: the patient, the prescriber and one MedicationRequest per drug, all under one group identifier. The
/// group becomes one prescription at reception, channel "eRx", for the pharmacist to match, review and fill like any other.
/// A Bundle sent again is the same prescription: the group and each drug are keyed on their identifiers. Withdrawing
/// a drug is the same MedicationRequest with status "cancelled"; it is refused with 409 once the drug has been filled.
/// The token names the clinic (clinic_slug) and every identifier must belong to it.
/// </summary>
public static class FhirEndpoints
{
    public const string OntarioHealthCard = "https://fhir.infoway-inforoute.ca/NamingSystem/ca-on-patient-hcn";
    public const string Cpso = "https://fhir.infoway-inforoute.ca/NamingSystem/ca-on-license-physician";
    public const string Din = "http://hl7.org/fhir/NamingSystem/ca-hc-din";
    private const string HealthCardVersionExtension = "urn:clinichub:extension:health-card-version";

    private class Invalid(string message, int status = StatusCodes.Status400BadRequest, string code = "invalid") : Exception(message)
    {
        public int Status { get; } = status;
        public string Code { get; } = code;
    }

    private record Line(string ExternalId, string? Din, string Text, string Sig, decimal Quantity, short? DaysSupply, byte Refills, bool NoSubstitution, DateOnly Written, string? Note);

    public static void MapFhirEndpoints(this IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<IOptions<ClinicHubOptions>>().Value.Enabled)
            return;
        app.MapPost("/fhir", Receive).RequireAuthorization(ClinicHubService.PrescriptionsPolicy).ExcludeFromDescription();
    }

    private static async Task<IResult> Receive(HttpRequest request, ClaimsPrincipal hub, PharmaDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        var slug = hub.FindFirstValue(ClinicHubService.ClinicClaim)!;
        try
        {
            JsonNode? bundle;
            try { bundle = await JsonNode.ParseAsync(request.Body, cancellationToken: ct); }
            catch (System.Text.Json.JsonException) { throw new Invalid("The body is not JSON."); }
            if ((string?)bundle?["resourceType"] != "Bundle" || (string?)bundle["type"] != "transaction")
                throw new Invalid("Expected a FHIR transaction Bundle.");
            var entries = bundle["entry"]?.AsArray().Select(e => e?["resource"]).ToList() ?? [];
            if (entries.Count == 0 || entries.Any(r => r is null))
                throw new Invalid("The Bundle has no entries.");
            var requests = entries.Where(r => (string?)r!["resourceType"] == "MedicationRequest").ToList();
            if (entries is [{ } only] && (string?)only["resourceType"] == "Patient" && only["link"] is not null)
                return Results.Text((await MergeAsync(db, clock, slug, only, ct)).ToJsonString(), "application/fhir+json");
            var response = requests.Count > 0 && requests.Count == entries.Count && requests.All(r => (string?)r!["status"] == "cancelled")
                ? await CancelAsync(db, clock, slug, requests!, ct)
                : await ReceiveAsync(db, clock, hub, slug, bundle, entries!, ct);
            return Results.Text(response.ToJsonString(), "application/fhir+json");
        }
        catch (Invalid e)
        {
            return Outcome(e.Message, e.Status, e.Code);
        }
        catch (DbUpdateException e) when (e.InnerException?.Message.Contains("UX_Prescription", StringComparison.Ordinal) == true)
        {
            // The same Bundle arrived twice at once; the sender tries again and finds the first one.
            loggers.CreateLogger("Fhir").LogInformation("Concurrent duplicate prescription Bundle from {Clinic}", slug);
            return Outcome("The same prescription is being received; try again.", StatusCodes.Status409Conflict, "conflict");
        }
    }

    private static async Task<JsonObject> ReceiveAsync(PharmaDbContext db, TimeProvider clock, ClaimsPrincipal hub, string slug, JsonNode bundle, List<JsonNode> entries, CancellationToken ct)
    {
        var patientResource = entries.FirstOrDefault(r => (string?)r["resourceType"] == "Patient");
        var practitioner = entries.FirstOrDefault(r => (string?)r["resourceType"] == "Practitioner");
        var requests = entries.Where(r => (string?)r["resourceType"] == "MedicationRequest").ToList();
        var groupSystem = (string?)bundle["identifier"]?["system"];
        var groupValue = (string?)bundle["identifier"]?["value"];
        if (patientResource is null || practitioner is null || requests.Count == 0 || string.IsNullOrWhiteSpace(groupValue) || string.IsNullOrWhiteSpace(groupSystem))
            throw new Invalid("The Bundle needs an identifier, a Patient, a Practitioner and at least one MedicationRequest.");
        if (requests.Any(r => (string?)r["status"] != "active"))
            throw new Invalid("A Bundle that withdraws prescriptions carries only cancelled MedicationRequests.");
        if (requests.Count > 20)
            throw new Invalid("At most 20 drugs in one Bundle.");
        var own = $"urn:clinichub:{slug}:";
        if (!groupSystem.StartsWith(own, StringComparison.Ordinal))
            throw new Invalid("The identifiers do not belong to the clinic of the token.", StatusCodes.Status403Forbidden, "forbidden");

        // Everything is checked before anything is saved.
        var lines = new List<Line>();
        foreach (var (r, i) in requests.Select((r, i) => (r, i)))
        {
            var id = r["identifier"]?[0];
            var system = (string?)id?["system"];
            var value = (string?)id?["value"];
            if (system is null || value is null || !system.StartsWith(own, StringComparison.Ordinal))
                throw new Invalid($"MedicationRequest {i + 1}: an identifier of the clinic is needed.");
            var coding = r["medicationCodeableConcept"]?["coding"]?.AsArray().FirstOrDefault(c => (string?)c?["system"] == Din);
            var din = (string?)coding?["code"];
            var text = ((string?)coding?["display"] ?? (string?)r["medicationCodeableConcept"]?["text"])?.Trim();
            var sig = ((string?)r["dosageInstruction"]?[0]?["text"])?.Trim();
            var dispense = r["dispenseRequest"];
            if (string.IsNullOrWhiteSpace(text) || text.Length > 200)
                throw new Invalid($"MedicationRequest {i + 1}: what was prescribed, at most 200 characters.");
            if (string.IsNullOrWhiteSpace(sig) || sig.Length > 300)
                throw new Invalid($"MedicationRequest {i + 1}: the directions, at most 300 characters.");
            var quantity = Number(dispense?["quantity"]?["value"]);
            if (quantity is null or <= 0 or > 10000)
                throw new Invalid($"MedicationRequest {i + 1}: a quantity above 0.");
            var days = Number(dispense?["expectedSupplyDuration"]?["value"]);
            if (days is < 1 or > 365)
                throw new Invalid($"MedicationRequest {i + 1}: days' supply 1 to 365.");
            var repeats = Number(dispense?["numberOfRepeatsAllowed"]) ?? 0;
            if (repeats is < 0 or > 12)
                throw new Invalid($"MedicationRequest {i + 1}: 0 to 12 refills.");
            var written = DateTime.TryParse((string?)r["authoredOn"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
                ? at : clock.GetUtcNow().UtcDateTime;
            lines.Add(new Line($"{system}|{value}", din, text, sig, quantity.Value, days is { } d ? (short)d : null, (byte)repeats,
                r["substitution"]?["allowedBoolean"]?.GetValue<bool>() == false, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(written, Toronto)),
                ((string?)r["note"]?[0]?["text"])?.Trim()));
        }
        if (lines.Select(l => l.ExternalId).Distinct().Count() != lines.Count)
            throw new Invalid("The same MedicationRequest twice in one Bundle.");
        var hcn = Identifier(patientResource, OntarioHealthCard);
        if (hcn is not null && !(hcn.Length == 10 && hcn.All(char.IsAsciiDigit)))
            throw new Invalid("The health card number is not 10 digits.");
        var name = patientResource["name"]?[0];
        var family = ((string?)name?["family"])?.Trim();
        var given = ((string?)name?["given"]?[0])?.Trim();
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(given) || !DateOnly.TryParse((string?)patientResource["birthDate"], CultureInfo.InvariantCulture, out var birth))
            throw new Invalid("The Patient needs a name and a birth date.");
        var licence = Identifier(practitioner, Cpso);
        var pname = practitioner["name"]?[0];
        var pfamily = ((string?)pname?["family"])?.Trim();
        if (string.IsNullOrWhiteSpace(licence) || licence.Length > 10 || string.IsNullOrWhiteSpace(pfamily))
            throw new Invalid("The Practitioner needs a college registration number and a name.");

        var receivedBy = $"ClinicHub ({slug})";
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // The patient: the health card, or else name and birth date; new to the pharmacy, a record is made.
        var patient = hcn is not null ? await db.Patients.SingleOrDefaultAsync(p => p.HealthCardNumber == hcn, ct) : null;
        patient ??= await db.Patients.FirstOrDefaultAsync(p => p.LastName == family && p.FirstName == given && p.DateOfBirth == birth, ct);
        if (patient is null)
        {
            db.Patients.Add(patient = new Patient
            {
                HealthCardNumber = hcn, HealthCardVersion = Trim(Version(patientResource), 2), FirstName = Trim(given, 60)!, LastName = Trim(family, 60)!, DateOfBirth = birth,
                Sex = (string?)patientResource["gender"] switch { "male" => "M", "female" => "F", "other" => "X", _ => "U" },
                Notes = $"Created from an electronic prescription from {slug}.", CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync(ct);
            db.AccessLog.Add(new AccessLog { OccurredAt = now, UserName = receivedBy, Action = "Create", PatientId = patient.PatientId, Context = "Patient created from an electronic prescription" });
        }
        var prescriber = await db.Prescribers.SingleOrDefaultAsync(p => p.LicenceBody == "CPSO" && p.LicenceNumber == licence, ct);
        if (prescriber is null)
        {
            db.Prescribers.Add(prescriber = new Prescriber
            {
                LicenceBody = "CPSO", LicenceNumber = licence, LastName = Trim(pfamily, 60)!, FirstName = Trim((string?)pname?["given"]?[0] ?? "", 60) ?? "",
                Clinic = Trim(hub.FindFirstValue(ClinicHubService.ClinicNameClaim) ?? slug, 120),
            });
            await db.SaveChangesAsync(ct);
        }

        var externalGroup = $"{groupSystem}|{groupValue}";
        var rx = await db.Prescriptions.Include(r => r.Items).SingleOrDefaultAsync(r => r.ExternalId == externalGroup, ct);
        var known = rx?.Items.ToDictionary(i => i.ExternalId ?? "") ?? [];
        var fresh = lines.Where(l => !known.ContainsKey(l.ExternalId)).ToList();
        if (fresh.Count > 0)
        {
            var drugs = await db.Drugs.AsNoTracking().Where(d => d.IsActive).Select(d => new { d.DrugId, d.Din }).ToListAsync(ct);
            int? DrugOf(Line l) => l.Din is null ? null : drugs.FirstOrDefault(d => d.Din == l.Din)?.DrugId;
            var checks = await CheckAsync(db, patient.PatientId, rx?.PrescriptionId, fresh.Min(l => l.Written), patient.DefaultPlanId,
                fresh.Select(l => new ItemBody(DrugOf(l), l.Text, l.Sig, l.Quantity, l.DaysSupply, l.Refills, l.NoSubstitution)).ToList(), Today(clock));
            if (rx is null)
            {
                var notes = Trim(string.Join(" ", fresh.Select(l => l.Note).Where(n => !string.IsNullOrEmpty(n))), 500);
                db.Prescriptions.Add(rx = new Prescription
                {
                    RxNumber = (await db.NextRxNumberAsync()).ToString(), PatientId = patient.PatientId, PrescriberId = prescriber.PrescriberId, WrittenDate = fresh.Min(l => l.Written),
                    ReceivedAt = now, Channel = "eRx", PlanId = patient.DefaultPlanId, Status = RxStatus.Received, ExternalId = externalGroup,
                    SourceMrn = Identifier(patientResource, $"urn:clinichub:{slug}:mrn") is { } mrn ? Trim($"{slug}|{mrn}", 60) : null,
                    Confidentiality = requests.Select(Confidentiality).Max(),
                    Notes = string.IsNullOrEmpty(notes) ? null : notes, ReceivedBy = receivedBy, UpdatedAt = now,
                });
            }
            var line = (byte)(rx.Items.Count == 0 ? 0 : rx.Items.Max(i => i.LineNumber));
            foreach (var (l, i) in fresh.Select((l, i) => (l, i)))
            {
                var item = new PrescriptionItem
                {
                    LineNumber = ++line, DrugId = DrugOf(l), DrugText = l.Text, Sig = l.Sig, SigExpanded = checks[i].SigExpanded, Quantity = l.Quantity,
                    DaysSupply = l.DaysSupply ?? checks[i].DaysSupply, RefillsAuthorized = l.Refills, RefillsRemaining = l.Refills, NoSubstitution = l.NoSubstitution,
                    Status = ItemStatus.Received, WarningsJson = System.Text.Json.JsonSerializer.Serialize(checks[i].Warnings, Json), ExternalId = l.ExternalId,
                };
                rx.Items.Add(item);
                known[l.ExternalId] = item;
            }
            await db.SaveChangesAsync(ct);
            await Settle(db, rx, clock);
            db.AccessLog.Add(new AccessLog { OccurredAt = now, UserName = receivedBy, Action = "Create", PatientId = patient.PatientId, Context = $"Prescription {rx.RxNumber} received (eRx, {fresh.Count} drugs)" });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);

        // One answer per entry, in the order sent: ClinicHub reads the prescriptions' places after the patient and the prescriber.
        var received = rx!;
        return Transaction(entries.Select(r => (string?)r["resourceType"] switch
        {
            "Patient" => ("200 OK", $"Patient/{patient.PatientId}"),
            "Practitioner" => ("200 OK", $"Practitioner/{prescriber.PrescriberId}"),
            "MedicationRequest" => (fresh.Any(f => f.ExternalId == ExternalOf(r)) ? "201 Created" : "200 OK", $"MedicationRequest/{received.RxNumber}-{known[ExternalOf(r)].LineNumber}"),
            var other => ("200 OK", $"{other}/0"),
        }));
    }

    private static async Task<JsonObject> CancelAsync(PharmaDbContext db, TimeProvider clock, string slug, List<JsonNode> requests, CancellationToken ct)
    {
        var own = $"urn:clinichub:{slug}:";
        var wanted = new List<(string ExternalId, string? Reason)>();
        foreach (var r in requests)
        {
            var system = (string?)r["identifier"]?[0]?["system"];
            var value = (string?)r["identifier"]?[0]?["value"];
            if (system is null || value is null || !system.StartsWith(own, StringComparison.Ordinal))
                throw new Invalid("An identifier of the clinic is needed to withdraw a prescription.");
            var why = ((string?)r["note"]?[0]?["text"])?.Trim();
            wanted.Add(($"{system}|{value}", string.IsNullOrEmpty(why) ? null : Trim(why, 200)));
        }
        var ids = wanted.Select(w => w.ExternalId).ToList();
        var items = await db.PrescriptionItems.Where(i => i.ExternalId != null && ids.Contains(i.ExternalId)).ToListAsync(ct);
        if (items.Count != ids.Count)
            throw new Invalid("No such prescription.", StatusCodes.Status404NotFound, "not-found");
        // Dispensing has begun for any one: nothing is changed.
        if (items.Any(i => i.Status is ItemStatus.Filled or ItemStatus.Verified or ItemStatus.Dispensed))
            throw new Invalid("Already being dispensed: the pharmacy cannot cancel it. Call the pharmacy.", StatusCodes.Status409Conflict, "business-rule");

        var now = clock.GetUtcNow().UtcDateTime;
        var locations = new List<string>();
        foreach (var (externalId, reason) in wanted)
        {
            var item = items.Single(i => i.ExternalId == externalId);
            var rx = await db.Prescriptions.SingleAsync(r => r.PrescriptionId == item.PrescriptionId, ct);
            if (item.Status != ItemStatus.Cancelled)
            {
                item.Status = ItemStatus.Cancelled;
                item.CancelReason = reason ?? "Cancelled by the prescriber's clinic";
                await Settle(db, rx, clock);
                db.AccessLog.Add(new AccessLog { OccurredAt = now, UserName = $"ClinicHub ({slug})", Action = "Update", PatientId = rx.PatientId, Context = $"Prescription {rx.RxNumber} line {item.LineNumber} cancelled by the clinic" });
            }
            locations.Add($"MedicationRequest/{rx.RxNumber}-{item.LineNumber}");
        }
        await db.SaveChangesAsync(ct);
        return Transaction(locations.Select(l => ("200 OK", l)));
    }

    public const string ConfidentialitySystem = "http://terminology.hl7.org/CodeSystem/v3-Confidentiality";

    /// <summary>A prescription's label: R or V, any other code of that system treated as R (the contract's rule); null for none.</summary>
    public static string? Confidentiality(JsonNode? resource)
    {
        var codes = resource?["meta"]?["security"]?.AsArray().Where(c => (string?)c?["system"] == ConfidentialitySystem)
            .Select(c => (string?)c!["code"]).Where(c => !string.IsNullOrEmpty(c)).ToList() ?? [];
        if (codes.Count == 0 || codes.All(c => c is "U" or "L" or "M" or "N"))
            return null;
        return codes.Contains("V") ? "V" : "R";
    }

    /// <summary>
    /// ClinicHub merged two records (IntegrationHub's flow 6). The pharmacy keeps its own patients (by health card, or
    /// name and birth date), so the merge moves the clinic's MRN on its prescriptions to the survivor's; two pharmacy
    /// patients behind one clinic MRN are written to the access log as a possible duplicate, not merged on the clinic's
    /// word. 200 either way, and for a record never seen here.
    /// </summary>
    private static async Task<JsonObject> MergeAsync(PharmaDbContext db, TimeProvider clock, string slug, JsonNode patient, CancellationToken ct)
    {
        var system = $"urn:clinichub:{slug}:mrn";
        var merged = Identifier(patient, system);
        var replacedBy = patient["link"]?.AsArray().FirstOrDefault(l => (string?)l?["type"] == "replaced-by")?["other"]?["identifier"];
        var survivor = (string?)replacedBy?["system"] == system ? ((string?)replacedBy?["value"])?.Trim() : null;
        if (string.IsNullOrWhiteSpace(merged) || string.IsNullOrWhiteSpace(survivor) || merged == survivor)
            throw new Invalid($"A merge names the merged and the surviving MRN, both {system}.");
        var (from, to) = (Trim($"{slug}|{merged}", 60), Trim($"{slug}|{survivor}", 60));
        var moved = await db.Prescriptions.Where(p => p.SourceMrn == from).ToListAsync(ct);
        if (moved.Count > 0)
        {
            var theirs = await db.Prescriptions.Where(p => p.SourceMrn == to).Select(p => p.PatientId).Distinct().ToListAsync(ct);
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var p in moved)
            {
                p.SourceMrn = to;
                p.UpdatedAt = now;
            }
            var patients = moved.Select(p => p.PatientId).Concat(theirs).Distinct().ToList();
            db.AccessLog.Add(new AccessLog
            {
                OccurredAt = now, UserName = $"ClinicHub ({slug})", Action = "Update", PatientId = moved[0].PatientId,
                Context = $"Clinic merged {merged} into {survivor}: {moved.Count} prescriptions follow"
                    + (patients.Count > 1 ? $"; possible duplicate pharmacy patients {string.Join(", ", patients)}" : ""),
            });
            await db.SaveChangesAsync(ct);
        }
        return Transaction([("200 OK", $"Patient/{survivor}")]);
    }

    private static string ExternalOf(JsonNode r) => $"{(string?)r["identifier"]![0]!["system"]}|{(string?)r["identifier"]![0]!["value"]}";

    private static string? Identifier(JsonNode resource, string system) =>
        resource["identifier"]?.AsArray().FirstOrDefault(i => (string?)i?["system"] == system) is { } id ? ((string?)id["value"])?.Trim() : null;

    private static string? Version(JsonNode patient) =>
        patient["identifier"]?.AsArray().FirstOrDefault(i => (string?)i?["system"] == OntarioHealthCard)?["extension"]?.AsArray()
            .FirstOrDefault(e => (string?)e?["url"] == HealthCardVersionExtension)?["valueString"]?.GetValue<string>();

    private static decimal? Number(JsonNode? n) => n is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;

    private static string? Trim(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;

    private static JsonObject Transaction(IEnumerable<(string Status, string Location)> results) => new()
    {
        ["resourceType"] = "Bundle", ["type"] = "transaction-response",
        ["entry"] = new JsonArray(results.Select(r => (JsonNode)new JsonObject { ["response"] = new JsonObject { ["status"] = r.Status, ["location"] = r.Location } }).ToArray()),
    };

    private static IResult Outcome(string diagnostics, int status = StatusCodes.Status400BadRequest, string code = "invalid") => Results.Text(new JsonObject
    {
        ["resourceType"] = "OperationOutcome",
        ["issue"] = new JsonArray(new JsonObject { ["severity"] = "error", ["code"] = code, ["diagnostics"] = diagnostics }),
    }.ToJsonString(), "application/fhir+json", statusCode: status);
}
