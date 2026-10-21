using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace PharmaFlow.Api.Auth;

/// <summary>
/// A prescription ClinicHub labelled (a patient's consent directive: R restricted, V very restricted) is closed to everyone
/// until someone with a reason opens it for themselves: a "break the glass" override that lasts <see cref="Lasts"/>, is
/// for one user and one prescription, and is written to the access log (who, what, why). Without it the prescription
/// answers 403 <c>restricted</c>; the retry with a reason is one call (<c>POST …/override</c>). Overrides live in memory:
/// a restart closes them, which is the safe way to fail.
/// </summary>
public class ConfidentialityGate(IMemoryCache cache, TimeProvider clock)
{
    public static readonly TimeSpan Lasts = TimeSpan.FromHours(1);
    public const int MinReason = 10;

    private static string Key(ClaimsPrincipal user, int prescriptionId) =>
        $"restricted|{user.FindFirstValue(ClinicHubSignIn.Clinic)}|{user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("name")}|{prescriptionId}";

    public bool IsOpen(ClaimsPrincipal user, int prescriptionId) => cache.TryGetValue(Key(user, prescriptionId), out _);

    public DateTime Open(ClaimsPrincipal user, int prescriptionId)
    {
        var until = clock.GetUtcNow().UtcDateTime + Lasts;
        cache.Set(Key(user, prescriptionId), true, new DateTimeOffset(until, TimeSpan.Zero));
        return until;
    }

    public static IResult Closed(string label) => Results.Json(
        new { code = "restricted", label, title = "The patient has a consent directive at the clinic: this prescription is restricted.", reasonNeeded = true },
        statusCode: StatusCodes.Status403Forbidden);

    public static bool ReasonOk(string? reason) => reason is not null && reason.Trim().Length >= MinReason;

    public record OverrideBody(string? Reason);
}
