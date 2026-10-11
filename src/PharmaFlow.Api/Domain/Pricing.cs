namespace PharmaFlow.Api.Domain;

/// <summary>
/// The price of a fill and who pays it. Drug cost is what the pharmacy paid for the lots used; the plan adds its
/// mark-up on that and its dispensing fee. The patient pays the plan's co-payment (a fixed amount, a percentage, or
/// both, never more than the total); the plan pays the rest. With no plan, the patient pays everything.
/// </summary>
public static class Pricing
{
    public record PlanTerms(decimal CopayAmount, decimal CopayPercent, decimal DispensingFee, decimal MarkupPercent);
    public record Price(decimal DrugCost, decimal Markup, decimal DispensingFee, decimal Total, decimal PlanPays, decimal PatientPays);

    /// <summary>Cash price when no plan is chosen: a 10 % mark-up and an 11.99 dispensing fee, the pharmacy's own settings.</summary>
    public static readonly PlanTerms Cash = new(0, 100, 11.99m, 10);

    public static Price For(decimal drugCost, PlanTerms? plan)
    {
        var terms = plan ?? Cash;
        var cost = Round(drugCost);
        var markup = Round(cost * terms.MarkupPercent / 100m);
        var fee = Round(terms.DispensingFee);
        var total = cost + markup + fee;
        var patient = Math.Min(total, Round(terms.CopayAmount + total * terms.CopayPercent / 100m));
        return new Price(cost, markup, fee, total, total - patient, patient);
    }

    /// <summary>
    /// The same price as invoice lines: the patient's share comes out of the dispensing fee first, then the drug, so the
    /// plan's invoice and the patient's add up to the price line by line.
    /// </summary>
    public static (decimal PlanDrug, decimal PlanFee, decimal PatientDrug, decimal PatientFee) Split(Price p)
    {
        var patientFee = Math.Min(p.PatientPays, p.DispensingFee);
        var patientDrug = p.PatientPays - patientFee;
        return (p.DrugCost + p.Markup - patientDrug, p.DispensingFee - patientFee, patientDrug, patientFee);
    }

    private static decimal Round(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);
}
