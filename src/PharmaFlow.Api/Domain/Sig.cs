using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaFlow.Api.Domain;

/// <summary>
/// Turns what a prescriber writes ("1 TAB PO BID X 10 D") into label text ("Take 1 tablet by mouth twice daily for
/// 10 days") with the pharmacy's SIG codes, and works out the days' supply where the instruction says how often.
/// Words that aren't codes are kept as written.
/// </summary>
public static partial class Sig
{
    public record Expanded(string Text, decimal? DosesPerDay, decimal? DoseQuantity);

    public static Expanded Expand(string sig, IReadOnlyDictionary<string, (string Expansion, decimal? DosesPerDay)> codes)
    {
        var words = Tokens().Matches(sig.Trim()).Select(m => m.Value).ToList();
        var output = new List<string>();
        decimal? perDay = null, dose = null;
        foreach (var w in words)
        {
            var key = w.ToUpperInvariant();
            if (codes.TryGetValue(key, out var code))
            {
                output.Add(code.Expansion);
                perDay ??= code.DosesPerDay;
            }
            else
            {
                if (dose is null && decimal.TryParse(w, NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
                    dose = n;
                output.Add(w.ToLowerInvariant() == w || char.IsDigit(w[0]) ? w : w.ToLowerInvariant());
            }
        }
        var text = string.Join(' ', output);
        // A label starts with a verb: "Take" for anything swallowed or counted, unless the prescriber wrote one.
        var verbs = new[] { "apply", "instill", "inhale", "insert", "use", "take", "inject", "chew", "dissolve" };
        if (text.Length > 0 && !verbs.Any(v => text.StartsWith(v, StringComparison.OrdinalIgnoreCase)))
            text = "Take " + text;
        else if (text.Length > 0)
            text = char.ToUpperInvariant(text[0]) + text[1..];
        return new Expanded(text, perDay, dose);
    }

    /// <summary>Days a quantity lasts: quantity ÷ (dose × doses a day), when both are known.</summary>
    public static short? DaysSupply(decimal quantity, Expanded sig) =>
        sig is { DosesPerDay: > 0, DoseQuantity: > 0 }
            ? (short)Math.Clamp(Math.Floor(quantity / (sig.DoseQuantity.Value * sig.DosesPerDay.Value)), 1, 365)
            : null;

    [GeneratedRegex(@"[A-Za-z0-9_]+(?:\.\d+)?|[^\sA-Za-z\d]+")]
    private static partial Regex Tokens();
}
