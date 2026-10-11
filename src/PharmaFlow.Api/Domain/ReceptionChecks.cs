using PharmaFlow.Api.Data;

namespace PharmaFlow.Api.Domain;

/// <summary>A finding at reception, for the pharmacist to review before the prescription is filled.</summary>
public record Warning(string Kind, string Severity, string Message);

public static class WarningKinds
{
    public const string Allergy = "Allergy";
    public const string Interaction = "Interaction";
    public const string Duplicate = "DuplicateTherapy";
    public const string Monitored = "MonitoredDrug";
    public const string LimitedUse = "LimitedUse";
    public const string Stale = "StalePrescription";
    public const string NotMatched = "NotMatched";
}

/// <summary>
/// What reception checks for each drug on a prescription: the patient's allergies (by ingredient or family), interactions
/// with the other drugs on it and with what the patient is already taking, the same ingredient twice, a drug recorded
/// in the Narcotics Monitoring System, an ODB Limited Use product, and an old prescription. It warns; the pharmacist
/// decides.
/// </summary>
public static class ReceptionChecks
{
    public record DrugFacts(int DrugId, string Name, int IngredientId, string Ingredient, string? AllergyClass, bool Monitored, bool LimitedUse);
    public record Current(int IngredientId, string Name, string RxNumber);

    public static List<Warning> For(
        DrugFacts? drug, string drugText, IReadOnlyList<DrugFacts> others, IReadOnlyList<Current> taking,
        IReadOnlyList<PatientAllergy> allergies, IReadOnlyList<Interaction> interactions, DateOnly written, DateOnly today, bool odbPlan)
    {
        var warnings = new List<Warning>();
        if (drug is null)
        {
            warnings.Add(new(WarningKinds.NotMatched, "Info", $"\"{drugText}\" is not matched to a product yet: choose one before filling."));
            return warnings;
        }

        foreach (var a in allergies)
        {
            if (a.IngredientId == drug.IngredientId)
                warnings.Add(new(WarningKinds.Allergy, "Major", $"Allergic to {drug.Ingredient}{Reaction(a)}."));
            else if (a.AllergyClass is not null && string.Equals(a.AllergyClass, drug.AllergyClass, StringComparison.OrdinalIgnoreCase))
                warnings.Add(new(WarningKinds.Allergy, "Major", $"Allergic to {a.AllergyClass}{Reaction(a)}; {drug.Ingredient} is one."));
            else if (a.Description is { Length: > 0 } d && (drug.Ingredient.Contains(d, StringComparison.OrdinalIgnoreCase) || d.Contains(drug.Ingredient, StringComparison.OrdinalIgnoreCase)))
                warnings.Add(new(WarningKinds.Allergy, "Major", $"Allergy recorded as \"{d}\"{Reaction(a)}."));
        }

        var against = others.Where(o => o.DrugId != drug.DrugId).Select(o => (o.IngredientId, Label: $"{o.Name} (this prescription)"))
            .Concat(taking.Select(t => (t.IngredientId, Label: $"{t.Name} (Rx {t.RxNumber})"))).ToList();
        foreach (var (ingredientId, label) in against)
        {
            if (ingredientId == drug.IngredientId)
                warnings.Add(new(WarningKinds.Duplicate, "Moderate", $"{drug.Ingredient} is also in {label}."));
            var (lo, hi) = drug.IngredientId < ingredientId ? (drug.IngredientId, ingredientId) : (ingredientId, drug.IngredientId);
            if (interactions.FirstOrDefault(i => i.IngredientAId == lo && i.IngredientBId == hi) is { } x)
                warnings.Add(new(WarningKinds.Interaction, x.Severity, $"With {label}: {x.Description}"));
        }

        if (drug.Monitored)
            warnings.Add(new(WarningKinds.Monitored, "Info", "Monitored drug: check the prescriber and patient identification, and record it in the Narcotics Monitoring System when dispensed."));
        if (drug.LimitedUse && odbPlan)
            warnings.Add(new(WarningKinds.LimitedUse, "Info", "ODB Limited Use product: the prescription needs the prescriber's LU reason code."));
        if (written < today.AddYears(-1))
            warnings.Add(new(WarningKinds.Stale, "Moderate", $"Written {written:yyyy-MM-dd}, more than a year ago."));
        return warnings;
    }

    private static string Reaction(PatientAllergy a) => a.Reaction is { Length: > 0 } r ? $" ({r})" : "";
}
