using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PharmaFlow.Api.Auth;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using PharmaFlow.Api.Data;
using PharmaFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace PharmaFlow.Tests;

public class SigTests
{
    private static readonly Dictionary<string, (string, decimal?)> Codes = new()
    {
        ["TAB"] = ("tablet", null), ["TABS"] = ("tablets", null), ["PO"] = ("by mouth", null), ["BID"] = ("twice daily", 2), ["TID"] = ("three times daily", 3),
        ["X"] = ("for", null), ["D"] = ("days", null), ["PRN"] = ("as needed", null), ["PUFFS"] = ("puffs", null), ["INH"] = ("by inhalation", null), ["Q4H"] = ("every 4 hours", 6),
    };

    [Fact]
    public void Codes_become_label_text_and_the_days_supply_follows()
    {
        var sig = Sig.Expand("1 TAB PO BID X 10 D", Codes);
        Assert.Equal("Take 1 tablet by mouth twice daily for 10 days", sig.Text);
        Assert.Equal((short)10, Sig.DaysSupply(20, sig));
        Assert.Equal((short)5, Sig.DaysSupply(30, Sig.Expand("2 tabs po tid", Codes)));
    }

    [Fact]
    public void Unknown_words_are_kept_and_as_needed_has_no_days_supply()
    {
        var sig = Sig.Expand("2 PUFFS INH Q4H PRN for wheeze", Codes);
        Assert.Equal("Take 2 puffs by inhalation every 4 hours as needed for wheeze", sig.Text);
        Assert.Null(Sig.DaysSupply(1, Sig.Expand("apply PRN", Codes)));
        Assert.StartsWith("Apply", Sig.Expand("apply PRN", Codes).Text);
    }
}

public class ReceptionCheckTests
{
    private static ReceptionChecks.DrugFacts Drug(int id, string ingredient, int ingredientId, string? family = null, bool monitored = false, bool lu = false) =>
        new(id, ingredient + " (demo)", ingredientId, ingredient, family, monitored, lu);

    private static readonly DateOnly Today = new(2026, 10, 11);

    [Fact]
    public void A_penicillin_allergy_flags_amoxicillin()
    {
        var w = ReceptionChecks.For(Drug(1, "Amoxicillin", 10, "Penicillins"), "Amox", [], [], [new PatientAllergy { AllergyClass = "Penicillins", Reaction = "Hives" }], [], Today, Today, false);
        var a = Assert.Single(w, x => x.Kind == WarningKinds.Allergy);
        Assert.Contains("Hives", a.Message);
    }

    [Fact]
    public void An_interaction_with_what_the_patient_takes_is_found_in_either_order()
    {
        var interactions = new List<Interaction> { new() { IngredientAId = 3, IngredientBId = 7, Severity = "Major", Description = "Bleeding risk." } };
        var w = ReceptionChecks.For(Drug(1, "Ibuprofen", 7), "Ibuprofen", [], [new(3, "Warfarin (demo)", "100001")], [], interactions, Today, Today, false);
        Assert.Contains(w, x => x.Kind == WarningKinds.Interaction && x.Severity == "Major" && x.Message.Contains("100001"));
    }

    [Fact]
    public void Monitored_limited_use_old_and_unmatched_drugs_are_called_out()
    {
        var w = ReceptionChecks.For(Drug(1, "Oxycodone", 5, monitored: true, lu: true), "Oxy", [], [], [], [], Today.AddYears(-2), Today, odbPlan: true);
        Assert.Contains(w, x => x.Kind == WarningKinds.Monitored);
        Assert.Contains(w, x => x.Kind == WarningKinds.LimitedUse);
        Assert.Contains(w, x => x.Kind == WarningKinds.Stale);
        Assert.Equal(WarningKinds.NotMatched, Assert.Single(ReceptionChecks.For(null, "Some cream", [], [], [], [], Today, Today, false)).Kind);
    }
}

/// <summary>The API over a database built from database/*.sql with the demo data, dropped afterwards.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _server = Environment.GetEnvironmentVariable("PHARMAFLOW_TEST_SQL") ?? "Server=.;Integrated Security=True;TrustServerCertificate=True";
    private readonly string _name = $"PharmaFlow_Test_{Guid.NewGuid():N}";
    public string ConnectionString => new SqlConnectionStringBuilder(_server) { InitialCatalog = _name }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:PharmaFlow", ConnectionString);
        // ClinicHub is a separate project: its keys are a stand-in, and tokens are signed here with the test key.
        builder.UseSetting("ClinicHub:Authority", HubIssuer);
        builder.UseSetting("ClinicHub:RequireHttpsMetadata", "false");
        builder.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(ClinicHubService.Scheme, o =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = HubIssuer };
            configuration.SigningKeys.Add(HubKey);
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        }));
    }

    public const string HubIssuer = "http://clinichub.test/";
    public static readonly RsaSecurityKey HubKey = new(RSA.Create(2048)) { KeyId = "test" };

    /// <summary>The token ClinicHub sends on its own: audience pharmaflow, a scope, and the clinic.</summary>
    public static string HubToken(string clinic = "harbourview", string scope = "prescriptions", string audience = "pharmaflow", SecurityKey? key = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = HubIssuer, Audience = audience, Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key ?? HubKey, SecurityAlgorithms.RsaSha256),
            Subject = new ClaimsIdentity([new Claim("sub", "clinichub"), new Claim("scope", scope), new Claim("clinic_slug", clinic), new Claim("clinic_name", "Harbourview Medical Centre (demo)")]),
        });

    public async Task InitializeAsync()
    {
        await using (var master = new SqlConnection(_server))
        {
            await master.OpenAsync();
            await new SqlCommand($"CREATE DATABASE [{_name}]", master).ExecuteNonQueryAsync();
        }
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "database"), "*.sql").Where(f => Regex.IsMatch(Path.GetFileName(f), @"^\d{3}_")).Order())
            foreach (var batch in Regex.Split(await File.ReadAllTextAsync(file), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch))
                    await new SqlCommand(batch, conn).ExecuteNonQueryAsync();
    }

    public async Task<T?> Scalar<T>(string sql)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        var v = await new SqlCommand(sql, conn).ExecuteScalarAsync();
        return v is null or DBNull ? default : (T)Convert.ChangeType(v, typeof(T));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(_server);
        await master.OpenAsync();
        await new SqlCommand($"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}];", master).ExecuteNonQueryAsync();
    }
}

public class ReceptionApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonNode> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }

    private Task<int> Id(string sql) => factory.Scalar<int>(sql);

    [Fact]
    public async Task A_prescription_is_received_checked_and_made_ready_to_fill()
    {
        var api = factory.CreateClient();
        var patient = await Id("SELECT PatientId FROM core.Patient WHERE LastName = N'Oduya'");        // penicillin allergy, ODB
        var prescriber = await Id("SELECT PrescriberId FROM core.Prescriber WHERE LicenceNumber = '99001'");
        var amox = await Id("SELECT DrugId FROM ref.Drug WHERE Din = '99000011'");
        var warfarin = await Id("SELECT DrugId FROM ref.Drug WHERE Din = '99000141'");
        var naproxen = await Id("SELECT DrugId FROM ref.Drug WHERE Din = '99000181'");

        var rx = await Json(await api.PostAsJsonAsync("/api/prescriptions", new
        {
            patientId = patient, prescriberId = prescriber, writtenDate = "2026-10-10", channel = "Paper", notes = (string?)null,
            items = new object[]
            {
                new { drugId = amox, drugText = "Amoxicillin 500 mg", sig = "1 CAP PO TID X 7 D", quantity = 21m, refillsAuthorized = 0, noSubstitution = false },
                new { drugId = warfarin, drugText = "Warfarin 5 mg", sig = "1 TAB PO QD", quantity = 30m, refillsAuthorized = 2, noSubstitution = false },
                new { drugId = naproxen, drugText = "Naproxen 500 mg", sig = "1 TAB PO BID PRN PAIN", quantity = 20m, refillsAuthorized = 0, noSubstitution = false },
                new { drugId = (int?)null, drugText = "Moisturizing cream", sig = "APPLY BID", quantity = 1m, refillsAuthorized = 0, noSubstitution = false },
            },
        }));
        Assert.Equal("ODB", await factory.Scalar<string>($"SELECT p.Code FROM rx.Prescription r JOIN ref.[Plan] p ON p.PlanId = r.PlanId WHERE r.PrescriptionId = {rx["prescriptionId"]}"));
        var items = rx["items"]!.AsArray();
        Assert.Equal("Take 1 capsule by mouth three times daily for 7 days", (string)items[0]!["sigExpanded"]!);
        Assert.Equal(7, (int)items[0]!["daysSupply"]!);
        Assert.Contains(items[0]!["warnings"]!.AsArray(), w => (string)w!["kind"]! == "Allergy");
        Assert.Contains(items[1]!["warnings"]!.AsArray(), w => (string)w!["kind"]! == "Interaction" && (string)w["severity"]! == "Major");
        Assert.Contains(items[3]!["warnings"]!.AsArray(), w => (string)w!["kind"]! == "NotMatched");

        // The pharmacist cancels the amoxicillin (allergy), reviews the rest, and matches the cream.
        var amoxItem = (int)items[0]!["itemId"]!;
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"/api/prescriptions/items/{amoxItem}/cancel", new { reason = "" })).StatusCode);
        await Json(await api.PostAsJsonAsync($"/api/prescriptions/items/{amoxItem}/cancel", new { reason = "Penicillin allergy; prescriber called for an alternative" }));
        await Json(await api.PostAsync($"/api/prescriptions/items/{(int)items[1]!["itemId"]!}/review", null));
        var cream = (int)items[3]!["itemId"]!;
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsync($"/api/prescriptions/items/{cream}/review", null)).StatusCode);
        var hydrocortisone = await Id("SELECT DrugId FROM ref.Drug WHERE Din = '99000341'");
        var matched = await Json(await api.PostAsJsonAsync($"/api/prescriptions/items/{cream}/match", new { drugId = hydrocortisone }));
        Assert.DoesNotContain(matched["items"]![3]!["warnings"]!.AsArray(), w => (string)w!["kind"]! == "NotMatched");
        var after = await Json(await api.PostAsync($"/api/prescriptions/items/{cream}/review", null));
        Assert.Equal("InProgress", (string)after["status"]!);
        Assert.Equal("ReadyToFill", (string)after["items"]![1]!["status"]!);
    }

    [Fact]
    public async Task Reception_refuses_what_it_must()
    {
        var api = factory.CreateClient();
        var patient = await Id("SELECT PatientId FROM core.Patient WHERE LastName = N'Anand'");
        var prescriber = await Id("SELECT PrescriberId FROM core.Prescriber WHERE LicenceNumber = '99002'");
        var oxy = await Id("SELECT DrugId FROM ref.Drug WHERE Din = '99000201'");
        var res = await api.PostAsJsonAsync("/api/prescriptions", new
        {
            patientId = patient, prescriberId = prescriber, writtenDate = "2026-10-10", channel = "Fax",
            items = new[] { new { drugId = oxy, drugText = "Oxycodone 5 mg", sig = "1 TAB PO Q6H PRN PAIN", quantity = 20m, refillsAuthorized = 1, noSubstitution = false } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("refills", await res.Content.ReadAsStringAsync());

        var future = await api.PostAsJsonAsync("/api/prescriptions", new
        {
            patientId = patient, prescriberId = prescriber, writtenDate = "2099-01-01", channel = "Paper",
            items = new[] { new { drugId = (int?)null, drugText = "x", sig = "UD", quantity = 1m, refillsAuthorized = 0, noSubstitution = false } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
    }

    [Fact]
    public async Task Base_definitions_and_the_catalog_are_validated()
    {
        var api = factory.CreateClient();
        var all = await Json(await api.GetAsync("/api/ref"));
        Assert.Contains(all["schedules"]!.AsArray(), s => (string)s!["code"]! == "N" && (bool)s["monitored"]!);
        await Json(await api.PutAsJsonAsync("/api/ref/sig-codes/Q2H", new { expansion = "every 2 hours", dosesPerDay = 12m }));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PutAsJsonAsync("/api/ref/sig-codes/BAD CODE", new { expansion = "x" })).StatusCode);

        var ingredient = await Id("SELECT IngredientId FROM ref.Ingredient WHERE Name = N'Metformin'");
        var bad = await api.PostAsJsonAsync("/api/drugs", new
        {
            din = "1234", brandName = "Metformin ER", ingredientId = ingredient, strength = 500m, strengthUnit = "MG", formCode = "TABER", routeCode = "PO",
            scheduleCode = "I", packSize = 100, dispensingUnit = "TAB", unitCost = 0.05m, odbBenefit = true, odbLimitedUse = false,
        });
        Assert.Contains("din", await bad.Content.ReadAsStringAsync());
        var created = await Json(await api.PostAsJsonAsync("/api/drugs", new
        {
            din = "99000999", brandName = "Metformin ER (test)", ingredientId = ingredient, strength = 500m, strengthUnit = "MG", formCode = "TABER", routeCode = "PO",
            scheduleCode = "I", packSize = 100, dispensingUnit = "TAB", unitCost = 0.05m, odbBenefit = true, odbLimitedUse = false, interchangeGroup = "metf500er",
        }));
        Assert.Equal("METF500ER", (string)created["interchangeGroup"]!);
        var found = (await Json(await api.GetAsync("/api/drugs?q=Metformin"))).AsArray();
        Assert.True(found.Count >= 2);
    }
}

public class PricingTests
{
    [Fact]
    public void Odb_pays_all_but_the_copay_which_comes_out_of_the_fee_first()
    {
        var p = Pricing.For(12.345m, new Pricing.PlanTerms(2.00m, 0, 8.83m, 8));
        Assert.Equal((12.35m, 0.99m, 8.83m, 22.17m), (p.DrugCost, p.Markup, p.DispensingFee, p.Total));
        Assert.Equal((20.17m, 2.00m), (p.PlanPays, p.PatientPays));
        var s = Pricing.Split(p);
        Assert.Equal((13.34m, 6.83m, 0m, 2.00m), s);
    }

    [Fact]
    public void Cash_pays_everything_and_a_copay_never_exceeds_the_total()
    {
        var cash = Pricing.For(5m, null);
        Assert.Equal(cash.Total, cash.PatientPays);
        Assert.Equal(0, cash.PlanPays);
        var tiny = Pricing.For(0.10m, new Pricing.PlanTerms(50m, 0, 0, 0));
        Assert.Equal(tiny.Total, tiny.PatientPays);
    }
}

public class DispensingApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonNode> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }

    [Fact]
    public async Task Stock_is_received_dispensed_by_expiry_priced_by_the_plan_and_sent_to_the_accounting()
    {
        var api = factory.CreateClient();
        var metformin = await factory.Scalar<int>("SELECT DrugId FROM ref.Drug WHERE Din = '99000061'");
        var supplier = await factory.Scalar<int>("SELECT SupplierId FROM ref.Supplier WHERE Code = 'WHOLESALE-A'");
        await Json(await api.PostAsJsonAsync("/api/purchases", new
        {
            supplierId = supplier, invoiceNumber = "INV-1001", invoiceDate = "2026-10-01", suppliesAmount = 100m, suppliesHst = 13m,
            lines = new[]
            {
                new { drugId = metformin, lotNumber = "LATE", expiryDate = "2028-01-31", quantity = 500m, unitCost = 0.0300m },
                new { drugId = metformin, lotNumber = "SOON", expiryDate = "2027-01-31", quantity = 40m, unitCost = 0.0250m },
            },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync("/api/purchases", new { supplierId = supplier, invoiceNumber = "INV-1001", invoiceDate = "2026-10-01", suppliesAmount = 0m, suppliesHst = 0m,
            lines = new[] { new { drugId = metformin, lotNumber = "X", expiryDate = "2028-01-31", quantity = 1m, unitCost = 0.01m } } })).StatusCode);

        var patient = await factory.Scalar<int>("SELECT PatientId FROM core.Patient WHERE LastName = N'Oduya'");   // ODB
        var prescriber = await factory.Scalar<int>("SELECT PrescriberId FROM core.Prescriber WHERE LicenceNumber = '99001'");
        var rx = await Json(await api.PostAsJsonAsync("/api/prescriptions", new
        {
            patientId = patient, prescriberId = prescriber, writtenDate = "2026-10-10", channel = "Paper",
            items = new[] { new { drugId = metformin, drugText = "Metformin 500 mg", sig = "1 TAB PO BID", quantity = 60m, refillsAuthorized = 1, noSubstitution = false } },
        }));
        var item = (int)rx["items"]![0]!["itemId"]!;
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync($"/api/prescriptions/items/{item}/dispense", new { paymentMethod = "Debit" })).StatusCode);   // not reviewed
        await Json(await api.PostAsync($"/api/prescriptions/items/{item}/review", null));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"/api/prescriptions/items/{item}/dispense", new { })).StatusCode);   // the co-pay needs a method

        var first = await Json(await api.PostAsJsonAsync($"/api/prescriptions/items/{item}/dispense", new { paymentMethod = "Debit" }));
        // 40 from the lot expiring first at 0.025, then 20 at 0.03: 1.00 + 0.60.
        Assert.Equal(1.60m, (decimal)first["price"]!["drugCost"]!);
        Assert.Equal(2.00m, (decimal)first["price"]!["patientPays"]!);
        Assert.Equal(0m, await factory.Scalar<decimal>("SELECT QuantityOnHand FROM inv.StockLot WHERE LotNumber = 'SOON'"));
        var refill = await Json(await api.PostAsJsonAsync($"/api/prescriptions/items/{item}/dispense", new { paymentMethod = "Cash" }));
        Assert.Equal(0, (int)refill["refillsRemaining"]!);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync($"/api/prescriptions/items/{item}/dispense", new { paymentMethod = "Cash" })).StatusCode);

        await using var db = new PharmaDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PharmaDbContext>()
            .UseSqlServer(factory.ConnectionString).Options);
        var docs = await FinanceFeed.BuildAsync(db, "pharmacy");
        var bill = docs.Single(d => (string)d["kind"]! == "Bill");
        Assert.Equal(16.00m, (decimal)bill["lines"]!.AsArray().Single(l => (string)l!["category"]! == "Inventory.Drugs")!["amount"]!);
        Assert.Equal(13m, (decimal)bill["lines"]!.AsArray().Single(l => (string)l!["category"]! == "Medical Supplies")!["tax"]!);
        var dispenseId = (int)first["dispenseId"]!;
        var odb = docs.Single(d => (string)d["externalId"]! == $"dispense:{dispenseId}:plan");
        Assert.Equal("ODB", (string)odb["party"]!["type"]!);
        Assert.Contains(docs, d => (string)d["externalId"]! == $"dispense:{dispenseId}:paid" && (string)d["method"]! == "Debit");
        var cost = docs.Single(d => (string)d["externalId"]! == $"dispense:{dispenseId}:cost");
        Assert.Equal(1.60m, (decimal)cost["lines"]![0]!["amount"]!);
        Assert.DoesNotContain("Oduya", new JsonArray(docs.Select(d => (JsonNode)d.DeepClone()).ToArray()).ToJsonString());
    }
}
