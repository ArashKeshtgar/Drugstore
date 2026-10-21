namespace PharmaFlow.Api.Data;

// Classes for the tables in database/*.sql (the scripts are the schema; EF only reads and writes).

public class DosageForm { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? NameFr { get; set; } public bool IsActive { get; set; } = true; }
public class RouteOfAdmin { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? NameFr { get; set; } public bool IsActive { get; set; } = true; }
public class Unit { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string Kind { get; set; } = "Strength"; public bool IsActive { get; set; } = true; }

public class Schedule
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool RequiresPrescription { get; set; }
    public bool Monitored { get; set; }
    public string? Description { get; set; }
}

public class SigCode
{
    public string Code { get; set; } = "";
    public string Expansion { get; set; } = "";
    public string? ExpansionFr { get; set; }
    public decimal? DosesPerDay { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Ingredient
{
    public int IngredientId { get; set; }
    public string Name { get; set; } = "";
    public string? AtcCode { get; set; }
    public string? AllergyClass { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Interaction
{
    public int IngredientAId { get; set; }
    public Ingredient IngredientA { get; set; } = null!;
    public int IngredientBId { get; set; }
    public Ingredient IngredientB { get; set; } = null!;
    public string Severity { get; set; } = "Moderate";
    public string Description { get; set; } = "";
}

public class Manufacturer { public int ManufacturerId { get; set; } public string Name { get; set; } = ""; public string? Country { get; set; } public bool IsActive { get; set; } = true; }

public class Supplier
{
    public int SupplierId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? AccountNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Plan
{
    public int PlanId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string PayerType { get; set; } = "Cash";
    public decimal CopayAmount { get; set; }
    public decimal CopayPercent { get; set; }
    public decimal DispensingFee { get; set; }
    public decimal MarkupPercent { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Drug
{
    public int DrugId { get; set; }
    public string Din { get; set; } = "";
    public string BrandName { get; set; } = "";
    public int IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;
    public decimal? Strength { get; set; }
    public string? StrengthUnit { get; set; }
    public string FormCode { get; set; } = "";
    public string RouteCode { get; set; } = "";
    public string ScheduleCode { get; set; } = "";
    public Schedule Schedule { get; set; } = null!;
    public int? ManufacturerId { get; set; }
    public Manufacturer? Manufacturer { get; set; }
    public int PackSize { get; set; } = 100;
    public string DispensingUnit { get; set; } = "TAB";
    public decimal UnitCost { get; set; }
    public bool OdbBenefit { get; set; }
    public bool OdbLimitedUse { get; set; }
    public string? InterchangeGroup { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDemo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Patient
{
    public int PatientId { get; set; }
    public string? HealthCardNumber { get; set; }
    public string? HealthCardVersion { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public string Sex { get; set; } = "U";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public int? DefaultPlanId { get; set; }
    public string? PlanMemberId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVer { get; set; } = [];
    public List<PatientAllergy> Allergies { get; set; } = [];
}

public class PatientAllergy
{
    public int AllergyId { get; set; }
    public int PatientId { get; set; }
    public int? IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public string? AllergyClass { get; set; }
    public string? Description { get; set; }
    public string? Reaction { get; set; }
    public string Severity { get; set; } = "Unknown";
    public DateTime RecordedAt { get; set; }
}

public class Prescriber
{
    public int PrescriberId { get; set; }
    public string LicenceBody { get; set; } = "CPSO";
    public string LicenceNumber { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Fax { get; set; }
    public string? Clinic { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class RxStatus
{
    public const string Received = "Received";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string OnHold = "OnHold";
    public const string Cancelled = "Cancelled";
}

public static class ItemStatus
{
    public const string Received = "Received";
    public const string ReadyToFill = "ReadyToFill";
    public const string Filled = "Filled";
    public const string Verified = "Verified";
    public const string Dispensed = "Dispensed";
    public const string OnHold = "OnHold";
    public const string Cancelled = "Cancelled";
}

public class Prescription
{
    public int PrescriptionId { get; set; }
    public string RxNumber { get; set; } = "";
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int PrescriberId { get; set; }
    public Prescriber Prescriber { get; set; } = null!;
    public DateOnly WrittenDate { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string Channel { get; set; } = "Paper";
    public int? PlanId { get; set; }
    public Plan? Plan { get; set; }
    public string Status { get; set; } = RxStatus.Received;
    public string? Notes { get; set; }
    public string? ExternalId { get; set; }

    /// <summary>An electronic prescription's patient as the clinic knows them: {clinic}|{MRN}, so a merge there is followed.</summary>
    public string? SourceMrn { get; set; }

    /// <summary>ClinicHub's confidentiality label (a consent directive): R restricted, V very restricted; null for none.</summary>
    public string? Confidentiality { get; set; }

    public string ReceivedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVer { get; set; } = [];
    public List<PrescriptionItem> Items { get; set; } = [];
}

public class PrescriptionItem
{
    public int ItemId { get; set; }
    public int PrescriptionId { get; set; }
    public byte LineNumber { get; set; }
    public int? DrugId { get; set; }
    public Drug? Drug { get; set; }
    public string DrugText { get; set; } = "";
    public string Sig { get; set; } = "";
    public string SigExpanded { get; set; } = "";
    public decimal Quantity { get; set; }
    public short? DaysSupply { get; set; }
    public byte RefillsAuthorized { get; set; }
    public byte RefillsRemaining { get; set; }
    public bool NoSubstitution { get; set; }
    public string Status { get; set; } = ItemStatus.Received;
    public string WarningsJson { get; set; } = "[]";
    public string? WarningsReviewedBy { get; set; }
    public DateTime? WarningsReviewedAt { get; set; }
    public string? CancelReason { get; set; }
    /// <summary>An electronic prescription's drug in the sending system, "{system}|{value}".</summary>
    public string? ExternalId { get; set; }
}

public class AccessLog
{
    public long AuditId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "View";
    public int? PatientId { get; set; }
    public string Context { get; set; } = "";
}

public class Purchase
{
    public int PurchaseId { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string InvoiceNumber { get; set; } = "";
    public DateOnly InvoiceDate { get; set; }
    public DateTime ReceivedAt { get; set; }
    public decimal DrugsAmount { get; set; }
    public decimal SuppliesAmount { get; set; }
    public decimal SuppliesHst { get; set; }
    public string ReceivedBy { get; set; } = "";
    public List<StockLot> Lots { get; set; } = [];
}

public class StockLot
{
    public int LotId { get; set; }
    public int DrugId { get; set; }
    public Drug Drug { get; set; } = null!;
    public string LotNumber { get; set; } = "";
    public DateOnly ExpiryDate { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal UnitCost { get; set; }
    public int? PurchaseId { get; set; }
    public DateTime ReceivedAt { get; set; }
}

public class Dispense
{
    public int DispenseId { get; set; }
    public int ItemId { get; set; }
    public int DrugId { get; set; }
    public bool IsRefill { get; set; }
    public decimal Quantity { get; set; }
    public DateTime DispensedAt { get; set; }
    public string DispensedBy { get; set; } = "";
    public int? PlanId { get; set; }
    public decimal DrugCost { get; set; }
    public decimal Markup { get; set; }
    public decimal DispensingFee { get; set; }
    public decimal Total { get; set; }
    public decimal PlanPays { get; set; }
    public decimal PatientPays { get; set; }
    public string? PaymentMethod { get; set; }
    public List<DispenseLot> Lots { get; set; } = [];
}

public class DispenseLot
{
    public int DispenseId { get; set; }
    public int LotId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public class FinanceExport
{
    public string ExternalId { get; set; } = "";
    public DateTime SentAt { get; set; }
}
