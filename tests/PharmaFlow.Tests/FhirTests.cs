using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;

namespace PharmaFlow.Tests;

/// <summary>Electronic prescriptions from ClinicHub: the Bundle it builds (contracts flow 5), received, resent, withdrawn.</summary>
public class FhirTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Group = "urn:clinichub:harbourview:prescription-group";
    private const string Rx = "urn:clinichub:harbourview:prescription";

    private static JsonObject Identifier(string system, string value) => new() { ["system"] = system, ["value"] = value };

    private static JsonObject Entry(string url, JsonObject resource) => new()
    {
        ["fullUrl"] = url, ["resource"] = resource, ["request"] = new JsonObject { ["method"] = "POST", ["url"] = (string)resource["resourceType"]! },
    };

    private static JsonObject Drug(string id, string group, string din, string display, string directions, int quantity, int days, int repeats, bool allowSubstitution) => new()
    {
        ["resourceType"] = "MedicationRequest", ["identifier"] = new JsonArray(Identifier(Rx, id)), ["groupIdentifier"] = Identifier(Group, group),
        ["status"] = "active", ["intent"] = "order",
        ["medicationCodeableConcept"] = new JsonObject { ["coding"] = new JsonArray(new JsonObject { ["system"] = FhirEndpointsDin, ["code"] = din, ["display"] = display }) },
        ["subject"] = new JsonObject { ["reference"] = "urn:uuid:patient" }, ["authoredOn"] = "2026-10-12T14:00:00Z",
        ["dosageInstruction"] = new JsonArray(new JsonObject { ["text"] = directions }),
        ["dispenseRequest"] = new JsonObject
        {
            ["quantity"] = new JsonObject { ["value"] = quantity, ["unit"] = "capsule" }, ["numberOfRepeatsAllowed"] = repeats,
            ["expectedSupplyDuration"] = new JsonObject { ["value"] = days, ["unit"] = "days" },
        },
        ["substitution"] = new JsonObject { ["allowedBoolean"] = allowSubstitution },
    };

    private const string FhirEndpointsDin = "http://hl7.org/fhir/NamingSystem/ca-hc-din";

    private static JsonObject Bundle(string group, string healthCard, string family, string given, string birth, string licence, params JsonObject[] drugs)
    {
        var entries = new JsonArray
        {
            Entry("urn:uuid:patient", new JsonObject
            {
                ["resourceType"] = "Patient",
                ["identifier"] = new JsonArray(Identifier("urn:clinichub:harbourview:mrn", "HV000071"), Identifier("https://fhir.infoway-inforoute.ca/NamingSystem/ca-on-patient-hcn", healthCard)),
                ["name"] = new JsonArray(new JsonObject { ["family"] = family, ["given"] = new JsonArray(given) }), ["gender"] = "female", ["birthDate"] = birth,
            }),
            Entry("urn:uuid:requester", new JsonObject
            {
                ["resourceType"] = "Practitioner", ["identifier"] = new JsonArray(Identifier("https://fhir.infoway-inforoute.ca/NamingSystem/ca-on-license-physician", licence)),
                ["name"] = new JsonArray(new JsonObject { ["family"] = "Patel", ["given"] = new JsonArray("Nadia"), ["prefix"] = new JsonArray("Dr.") }),
            }),
        };
        foreach (var d in drugs)
            entries.Add(Entry("urn:uuid:" + (string)d["identifier"]![0]!["value"]!, d));
        return new JsonObject { ["resourceType"] = "Bundle", ["type"] = "transaction", ["identifier"] = Identifier(Group, group), ["entry"] = entries };
    }

    private static JsonObject Cancel(string id, string din, string? reason) => new()
    {
        ["resourceType"] = "Bundle", ["type"] = "transaction",
        ["entry"] = new JsonArray(new JsonObject
        {
            ["resource"] = new JsonObject
            {
                ["resourceType"] = "MedicationRequest", ["identifier"] = new JsonArray(Identifier(Rx, id)), ["status"] = "cancelled", ["intent"] = "order",
                ["medicationCodeableConcept"] = new JsonObject { ["coding"] = new JsonArray(new JsonObject { ["system"] = FhirEndpointsDin, ["code"] = din }) },
                ["note"] = reason is null ? null : new JsonArray(new JsonObject { ["text"] = reason }),
            },
            ["request"] = new JsonObject { ["method"] = "PUT", ["url"] = $"MedicationRequest?identifier={Rx}|{id}" },
        }),
    };

    private async Task<(HttpStatusCode Status, JsonNode Body)> Post(JsonObject bundle, string? token = null)
    {
        var api = factory.CreateClient();
        if (token is not null)
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await api.PostAsync("/fhir", new StringContent(bundle.ToJsonString(), Encoding.UTF8, "application/fhir+json"));
        return (res.StatusCode, JsonNode.Parse(await res.Content.ReadAsStringAsync())!);
    }

    private static string Location(JsonNode body, int i) => (string)body["entry"]![i]!["response"]!["location"]!;

    [Fact]
    public async Task A_bundle_becomes_one_eRx_prescription_for_the_pharmacist_and_a_resend_is_the_same_one()
    {
        // Margaret Oduya is allergic to penicillins; amoxicillin must arrive flagged. 99000011 is amoxicillin, 99000071 atorvastatin.
        var bundle = Bundle("G101", "9876543217", "Oduya", "Margaret", "1948-04-12", "123456",
            Drug("101", "G101", "99000011", "Amoxicillin 500 mg capsule", "Take 1 capsule 3 times a day for 7 days", 21, 7, 0, true),
            Drug("102", "G101", "99000071", "Atorvastatin 20 mg tablet", "Take 1 tablet by mouth once a day", 30, 30, 2, false));
        var token = ApiFactory.HubToken();

        var (status, body) = await Post(bundle, token);
        Assert.Equal(HttpStatusCode.OK, status);
        var entries = body["entry"]!.AsArray();
        Assert.Equal(4, entries.Count);
        Assert.Equal("201 Created", (string)entries[2]!["response"]!["status"]!);
        var rxNumber = Location(body, 2).Split('/')[1].Split('-')[0];
        Assert.Equal($"MedicationRequest/{rxNumber}-1", Location(body, 2));
        Assert.Equal($"MedicationRequest/{rxNumber}-2", Location(body, 3));

        Assert.Equal("eRx", await factory.Scalar<string>($"SELECT Channel FROM rx.Prescription WHERE RxNumber = '{rxNumber}'"));
        Assert.Equal("Oduya", await factory.Scalar<string>($"SELECT p.LastName FROM rx.Prescription r JOIN core.Patient p ON p.PatientId = r.PatientId WHERE r.RxNumber = '{rxNumber}'"));
        Assert.Equal(1, await factory.Scalar<int>("SELECT COUNT(*) FROM core.Prescriber WHERE LicenceBody = 'CPSO' AND LicenceNumber = '123456' AND LastName = N'Patel'"));
        Assert.Equal("ODB", await factory.Scalar<string>($"SELECT pl.Code FROM rx.Prescription r JOIN ref.[Plan] pl ON pl.PlanId = r.PlanId WHERE r.RxNumber = '{rxNumber}'"));
        // The drugs are matched by DIN, with the written directions and the no-substitution flag as sent.
        Assert.Equal(1, await factory.Scalar<int>($"SELECT COUNT(*) FROM rx.PrescriptionItem i JOIN ref.Drug d ON d.DrugId = i.DrugId JOIN rx.Prescription r ON r.PrescriptionId = i.PrescriptionId WHERE r.RxNumber = '{rxNumber}' AND d.Din = '99000071' AND i.NoSubstitution = 1 AND i.RefillsAuthorized = 2 AND i.DaysSupply = 30"));
        Assert.Equal(1, await factory.Scalar<int>($"SELECT COUNT(*) FROM rx.PrescriptionItem i JOIN rx.Prescription r ON r.PrescriptionId = i.PrescriptionId WHERE r.RxNumber = '{rxNumber}' AND i.LineNumber = 1 AND i.WarningsJson LIKE '%Allergy%'"));
        Assert.Equal("Received", await factory.Scalar<string>($"SELECT [Status] FROM rx.Prescription WHERE RxNumber = '{rxNumber}'"));

        // Sent again (a retry after a lost answer): the same prescription, nothing added.
        var (again, repeat) = await Post(bundle, token);
        Assert.Equal(HttpStatusCode.OK, again);
        Assert.Equal("200 OK", (string)repeat["entry"]![2]!["response"]!["status"]!);
        Assert.Equal(Location(body, 2), Location(repeat, 2));
        Assert.Equal(Location(body, 3), Location(repeat, 3));
        Assert.Equal(1, await factory.Scalar<int>("SELECT COUNT(*) FROM rx.Prescription WHERE ExternalId = 'urn:clinichub:harbourview:prescription-group|G101'"));
        Assert.Equal(2, await factory.Scalar<int>($"SELECT COUNT(*) FROM rx.PrescriptionItem i JOIN rx.Prescription r ON r.PrescriptionId = i.PrescriptionId WHERE r.RxNumber = '{rxNumber}'"));
    }

    [Fact]
    public async Task A_patient_new_to_the_pharmacy_and_a_new_prescriber_get_records()
    {
        var bundle = Bundle("G102", "4444444441", "Tremblay", "Leila", "2013-07-12", "654321",
            Drug("201", "G102", "99000191", "Acetaminophen 500 mg tablet", "Take 1 tablet every 6 hours as needed", 20, 5, 0, true));
        var (status, _) = await Post(bundle, ApiFactory.HubToken());
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, await factory.Scalar<int>("SELECT COUNT(*) FROM core.Patient WHERE HealthCardNumber = '4444444441' AND LastName = N'Tremblay' AND Sex = 'F'"));
        Assert.Equal(1, await factory.Scalar<int>("SELECT COUNT(*) FROM core.Prescriber WHERE LicenceBody = 'CPSO' AND LicenceNumber = '654321'"));
        Assert.True(await factory.Scalar<int>("SELECT COUNT(*) FROM audit.AccessLog WHERE UserName = N'ClinicHub (harbourview)' AND Action = 'Create'") > 0);
    }

    [Fact]
    public async Task A_drug_can_be_withdrawn_until_it_is_filled_and_then_the_pharmacy_says_no()
    {
        var bundle = Bundle("G103", "9876543217", "Oduya", "Margaret", "1948-04-12", "123456",
            Drug("301", "G103", "99000071", "Atorvastatin 20 mg tablet", "Take 1 tablet by mouth once a day", 30, 30, 0, true),
            Drug("302", "G103", "99000091", "Ramipril 5 mg capsule", "Take 1 capsule once a day", 30, 30, 0, true));
        var token = ApiFactory.HubToken();
        var (_, body) = await Post(bundle, token);
        var rxNumber = Location(body, 2).Split('/')[1].Split('-')[0];

        // Before filling: withdrawn, with the clinic's reason.
        var (ok, answer) = await Post(Cancel("301", "99000071", "Changed my mind"), token);
        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.Equal(Location(body, 2), Location(answer, 0));
        Assert.Equal("Changed my mind", await factory.Scalar<string>($"SELECT CancelReason FROM rx.PrescriptionItem WHERE ExternalId = '{Rx}|301'"));
        Assert.Equal("Cancelled", await factory.Scalar<string>($"SELECT [Status] FROM rx.PrescriptionItem WHERE ExternalId = '{Rx}|301'"));
        Assert.Equal("Received", await factory.Scalar<string>($"SELECT [Status] FROM rx.Prescription WHERE RxNumber = '{rxNumber}'"));
        // Again: nothing more to do, still fine.
        Assert.Equal(HttpStatusCode.OK, (await Post(Cancel("301", "99000071", null), token)).Status);

        // The other drug has been filled: refused, and nothing changes.
        await factory.Scalar<int>($"UPDATE rx.PrescriptionItem SET [Status] = 'Filled' WHERE ExternalId = '{Rx}|302'; SELECT 1");
        var (conflict, refusal) = await Post(Cancel("302", "99000091", "Too late"), token);
        Assert.Equal(HttpStatusCode.Conflict, conflict);
        Assert.Contains("dispensed", (string)refusal["issue"]![0]!["diagnostics"]!);
        Assert.Equal("Filled", await factory.Scalar<string>($"SELECT [Status] FROM rx.PrescriptionItem WHERE ExternalId = '{Rx}|302'"));

        // A prescription that was never received.
        Assert.Equal(HttpStatusCode.NotFound, (await Post(Cancel("999", "99000091", null), token)).Status);
    }

    [Fact]
    public async Task Only_ClinicHubs_prescriptions_token_for_the_pharmacy_gets_in()
    {
        var bundle = Bundle("G104", "9876543217", "Oduya", "Margaret", "1948-04-12", "123456",
            Drug("401", "G104", "99000071", "Atorvastatin 20 mg tablet", "Take 1 tablet once a day", 30, 30, 0, true));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(bundle)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(bundle, ApiFactory.HubToken(audience: "labflow"))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(bundle, ApiFactory.HubToken(key: new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "test" }))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(bundle, ApiFactory.HubToken(scope: "orders"))).Status);
        // A token for another clinic cannot bring this clinic's identifiers.
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(bundle, ApiFactory.HubToken(clinic: "lakeshore"))).Status);
        Assert.Equal(0, await factory.Scalar<int>("SELECT COUNT(*) FROM rx.Prescription WHERE ExternalId LIKE '%G104'"));
    }

    [Fact]
    public async Task A_bundle_that_is_not_right_is_refused_whole()
    {
        var token = ApiFactory.HubToken();
        var bad = Bundle("G105", "9876543217", "Oduya", "Margaret", "1948-04-12", "123456",
            Drug("501", "G105", "99000071", "Atorvastatin 20 mg tablet", "Take 1 tablet once a day", 30, 30, 0, true),
            Drug("502", "G105", "99000091", "Ramipril 5 mg capsule", "Take 1 capsule once a day", 0, 30, 0, true));
        var (status, body) = await Post(bad, token);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("quantity", (string)body["issue"]![0]!["diagnostics"]!);
        Assert.Equal(0, await factory.Scalar<int>("SELECT COUNT(*) FROM rx.Prescription WHERE ExternalId LIKE '%G105'"));

        var notAHealthCard = Bundle("G106", "12345", "Oduya", "Margaret", "1948-04-12", "123456",
            Drug("601", "G106", "99000071", "Atorvastatin 20 mg tablet", "Take 1 tablet once a day", 30, 30, 0, true));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(notAHealthCard, token)).Status);
    }
}
