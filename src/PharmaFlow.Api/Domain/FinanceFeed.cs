using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Api.Data;

namespace PharmaFlow.Api.Domain;

public class FinanceOptions
{
    /// <summary>FinancePowerApp's address. Empty: nothing is sent to the accounting.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Development key for FinancePowerApp (X-Finance-Key). With sign-in through ClinicHub, PharmaFlow sends a ClinicHub
    /// client-credentials token for the scope "finance" instead, like the other applications.
    /// </summary>
    public string? DevKey { get; set; }

    /// <summary>The pharmacy's name for itself in the accounting.</summary>
    public string SourceKey { get; set; } = "pharmacy";
}

/// <summary>
/// The pharmacy's money to FinancePowerApp, the group's accounting, built from purchases and dispenses (each with an
/// id that never changes, marked in billing.FinanceExport once accepted). No health information: no patient, no drug.
/// <list type="bullet">
/// <item>A supplier's invoice: a bill, drugs to inventory (zero-rated), supplies with their HST (an input tax credit:
/// the pharmacy makes zero-rated supplies).</item>
/// <item>A dispense: an invoice to the plan for its share (ODB, or the insurer), an invoice to the patient for theirs
/// and its receipt, prescription drugs and dispensing fee both zero-rated; and the cost of the lots used out of
/// inventory.</item>
/// </list>
/// </summary>
public class FinanceFeed(IHttpClientFactory http, Microsoft.Extensions.Options.IOptionsMonitor<FinanceOptions> options, IServiceScopeFactory scopes, TimeProvider clock, ILogger<FinanceFeed> logger,
    Microsoft.Extensions.Options.IOptions<PharmaFlow.Api.Auth.ClinicHubOptions> hub, Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions> oidc)
{
    public const string HttpClientName = "finance";
    private string? _token;
    private DateTime _tokenExpires;
    public bool Connected => !string.IsNullOrWhiteSpace(options.CurrentValue.BaseUrl);

    public async Task<int> SendAsync(CancellationToken ct = default)
    {
        if (!Connected) return 0;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
        var sent = (await db.FinanceExports.AsNoTracking().Select(x => x.ExternalId).ToListAsync(ct)).ToHashSet();
        var pending = (await BuildAsync(db, options.CurrentValue.SourceKey, ct)).Where(d => !sent.Contains((string)d["externalId"]!)).ToList();
        var accepted = 0;
        foreach (var chunk in pending.Chunk(100))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, options.CurrentValue.BaseUrl!.TrimEnd('/') + "/api/integration/documents")
                {
                    Content = JsonContent.Create(new JsonArray(chunk.Select(d => (JsonNode)d.DeepClone()).ToArray())),
                };
                if (hub.Value.SignInEnabled)
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
                else if (options.CurrentValue.DevKey is { Length: > 0 } key)
                    request.Headers.Add("X-Finance-Key", key);
                using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
                if ((int)response.StatusCode == 401)
                    _token = null;
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("FinancePowerApp refused {Count} documents: {Status} {Body}", chunk.Length, (int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                    break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning("FinancePowerApp could not be reached: {Error}", ex.Message);
                break;
            }
            var now = clock.GetUtcNow().UtcDateTime;
            db.FinanceExports.AddRange(chunk.Select(d => new FinanceExport { ExternalId = (string)d["externalId"]!, SentAt = now }));
            await db.SaveChangesAsync(ct);
            accepted += chunk.Length;
        }
        return accepted;
    }

    /// <summary>PharmaFlow as itself: a client-credentials token from ClinicHub for the scope "finance", kept until shortly before it expires.</summary>
    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token is not null && clock.GetUtcNow().UtcDateTime < _tokenExpires)
            return _token;
        var configuration = await oidc.Get(PharmaFlow.Api.Auth.ClinicHubSignIn.Scheme).ConfigurationManager!.GetConfigurationAsync(ct);
        using var response = await http.CreateClient(HttpClientName).PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = hub.Value.ClientId, ["client_secret"] = hub.Value.ClientSecret ?? "", ["scope"] = "finance",
        }), ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"ClinicHub refused PharmaFlow's credentials for finance ({(int)response.StatusCode}).");
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        _token = json.RootElement.GetProperty("access_token").GetString();
        var seconds = json.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;
        _tokenExpires = clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Max(30, seconds - 60));
        return _token!;
    }

    public static async Task<List<JsonObject>> BuildAsync(PharmaDbContext db, string key, CancellationToken ct = default)
    {
        var docs = new List<JsonObject>();
        var purchases = await db.Purchases.AsNoTracking().Include(p => p.Supplier).OrderBy(p => p.PurchaseId).ToListAsync(ct);
        foreach (var p in purchases)
        {
            var lines = new List<JsonObject>();
            if (p.DrugsAmount > 0) lines.Add(Line("Inventory.Drugs", p.DrugsAmount, "ZERO"));
            if (p.SuppliesAmount > 0) lines.Add(Line("Medical Supplies", p.SuppliesAmount, "HST13", p.SuppliesHst));
            if (lines.Count > 0)
                docs.Add(Doc(key, $"purchase:{p.PurchaseId}", "Bill", p.InvoiceDate, $"PO-{p.PurchaseId}-{p.InvoiceNumber}"[..Math.Min(60, $"PO-{p.PurchaseId}-{p.InvoiceNumber}".Length)],
                    Party("Supplier", p.Supplier.Code, p.Supplier.Name), [.. lines], memo: $"Supplier invoice {p.InvoiceNumber}"));
        }

        var dispenses = await db.Dispenses.AsNoTracking().OrderBy(d => d.DispenseId)
            .Select(d => new
            {
                d.DispenseId, d.DispensedAt, d.DrugCost, d.Markup, d.DispensingFee, d.Total, d.PlanPays, d.PatientPays, d.PaymentMethod,
                Plan = db.Plans.Where(p => p.PlanId == d.PlanId).Select(p => new { p.Code, p.Name, p.PayerType }).FirstOrDefault(),
                Rx = db.Prescriptions.Where(r => r.Items.Any(i => i.ItemId == d.ItemId)).Select(r => r.RxNumber).First(),
                Cost = d.Lots.Sum(l => l.Quantity * l.UnitCost),
            }).ToListAsync(ct);
        foreach (var d in dispenses)
        {
            var date = Local(d.DispensedAt);
            var reference = $"RX-{d.Rx}-{d.DispenseId}";
            var split = Pricing.Split(new Pricing.Price(d.DrugCost, d.Markup, d.DispensingFee, d.Total, d.PlanPays, d.PatientPays));
            if (d.PlanPays > 0 && d.Plan is { } plan)
            {
                var party = plan.PayerType == "ODB" ? Party("ODB", "ODB") : Party(plan.PayerType == "Insurer" ? "Insurer" : "Other", plan.Code, plan.Name);
                docs.Add(Doc(key, $"dispense:{d.DispenseId}:plan", "Invoice", date, $"{reference}-P", party,
                    Lines(split.PlanDrug, split.PlanFee), memo: $"Plan share, Rx {d.Rx}"));
            }
            if (d.PatientPays > 0)
            {
                docs.Add(Doc(key, $"dispense:{d.DispenseId}:patient", "Invoice", date, reference, Party("Patient", "PATIENT"),
                    Lines(split.PatientDrug, split.PatientFee), memo: $"Patient share, Rx {d.Rx}"));
                if (d.PaymentMethod is not (null or "Account"))
                    docs.Add(Doc(key, $"dispense:{d.DispenseId}:paid", "Receipt", date, reference, Party("Patient", "PATIENT"),
                        [Line(null, d.PatientPays, null, appliesTo: reference)], method: d.PaymentMethod, memo: $"Paid at pick-up, Rx {d.Rx}"));
            }
            var cost = decimal.Round(d.Cost, 2, MidpointRounding.AwayFromZero);
            if (cost > 0)
                docs.Add(Doc(key, $"dispense:{d.DispenseId}:cost", "Usage", date, reference, null,
                    [new JsonObject { ["category"] = "Pharmacy.COGS", ["amount"] = cost, ["offsetCategory"] = "Inventory.Drugs", ["department"] = "Pharmacy" }], memo: $"Cost of drugs, Rx {d.Rx}"));
        }
        return docs;
    }

    private static JsonObject[] Lines(decimal drug, decimal fee) =>
        [.. new[] { ("Pharmacy.Drug", drug), ("Pharmacy.DispensingFee", fee) }.Where(x => x.Item2 > 0).Select(x => Line(x.Item1, x.Item2, "ZERO"))];

    private static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    private static DateOnly Local(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Toronto));
    private static JsonObject Party(string type, string code, string? name = null) => new() { ["type"] = type, ["code"] = code, ["name"] = name };
    private static JsonObject Line(string? category, decimal amount, string? taxCode, decimal? tax = null, string? appliesTo = null) => new()
    {
        ["category"] = category, ["amount"] = amount, ["taxCode"] = taxCode, ["tax"] = tax, ["department"] = "Pharmacy", ["appliesTo"] = appliesTo,
    };
    private static JsonObject Doc(string key, string externalId, string kind, DateOnly date, string reference, JsonObject? party, JsonObject[] lines, string? method = null, string? memo = null) => new()
    {
        ["sourceKey"] = key, ["externalId"] = externalId, ["kind"] = kind, ["date"] = date.ToString("yyyy-MM-dd"), ["reference"] = reference, ["party"] = party,
        ["lines"] = new JsonArray(lines.Select(l => (JsonNode)l).ToArray()), ["method"] = method, ["memo"] = memo,
    };
}

public class FinanceWorker(FinanceFeed feed, ILogger<FinanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && feed.Connected)
        {
            try { await feed.SendAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Sending financial documents failed; trying again shortly."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
