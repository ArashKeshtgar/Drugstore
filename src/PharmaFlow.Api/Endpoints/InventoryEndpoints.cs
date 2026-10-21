using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Api.Auth;
using PharmaFlow.Api.Data;
using PharmaFlow.Api.Domain;
using static PharmaFlow.Api.Endpoints.RefEndpoints;

namespace PharmaFlow.Api.Endpoints;

/// <summary>
/// Purchasing and stock: a supplier's invoice received into lots (lot number, expiry, cost), and dispensing a drug that
/// is ready to fill: the oldest-expiring lots first, priced by the plan, the patient's share taken at the counter.
/// </summary>
public static class InventoryEndpoints
{
    public record PurchaseLine(int DrugId, string LotNumber, DateOnly ExpiryDate, decimal Quantity, decimal UnitCost);
    public record PurchaseBody(int SupplierId, string InvoiceNumber, DateOnly InvoiceDate, PurchaseLine[] Lines, decimal SuppliesAmount, decimal SuppliesHst);
    public record DispenseBody(decimal? Quantity, string? PaymentMethod);

    public static void MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(Policies.Any);

        api.MapGet("/purchases", async (PharmaDbContext db) => await db.Purchases.AsNoTracking().OrderByDescending(p => p.ReceivedAt).Take(200)
            .Select(p => new { p.PurchaseId, supplier = p.Supplier.Name, p.InvoiceNumber, p.InvoiceDate, p.ReceivedAt, p.DrugsAmount, p.SuppliesAmount, p.SuppliesHst,
                total = p.DrugsAmount + p.SuppliesAmount + p.SuppliesHst, lots = p.Lots.Count }).ToListAsync()).WithTags("Inventory");

        api.MapPost("/purchases", async (PurchaseBody b, ClaimsPrincipal user, PharmaDbContext db, TimeProvider clock) =>
        {
            var errors = new Dictionary<string, string[]>();
            void Check(bool bad, string field, string msg) { if (bad && !errors.ContainsKey(field)) errors[field] = [msg]; }
            var today = PrescriptionEndpoints.Today(clock);
            Check(!await db.Suppliers.AnyAsync(s => s.SupplierId == b.SupplierId && s.IsActive), "supplierId", "Choose the supplier.");
            Check(string.IsNullOrWhiteSpace(b.InvoiceNumber) || b.InvoiceNumber.Length > 40, "invoiceNumber", "The supplier's invoice number.");
            Check(await db.Purchases.AnyAsync(p => p.SupplierId == b.SupplierId && p.InvoiceNumber == b.InvoiceNumber.Trim()), "invoiceNumber", "This invoice was already received.");
            Check(b.InvoiceDate > today, "invoiceDate", "Not in the future.");
            Check(b.SuppliesAmount < 0 || b.SuppliesHst < 0 || b.SuppliesHst > decimal.Round(b.SuppliesAmount * 0.13m, 2) + 0.01m, "suppliesHst", "HST is at most 13 % of the supplies.");
            Check((b.Lines?.Length ?? 0) == 0 && b.SuppliesAmount == 0, "lines", "At least one drug, or supplies.");
            for (var i = 0; i < (b.Lines?.Length ?? 0); i++)
            {
                var l = b.Lines![i];
                Check(!await db.Drugs.AnyAsync(d => d.DrugId == l.DrugId), $"lines[{i}].drugId", "Unknown product.");
                Check(string.IsNullOrWhiteSpace(l.LotNumber) || l.LotNumber.Length > 30, $"lines[{i}].lotNumber", "The lot number on the box.");
                Check(l.ExpiryDate <= today, $"lines[{i}].expiryDate", "Already expired.");
                Check(l.Quantity <= 0, $"lines[{i}].quantity", "Above 0.");
                Check(l.UnitCost < 0 || decimal.Round(l.UnitCost, 4) != l.UnitCost, $"lines[{i}].unitCost", "Cost per unit, up to 4 decimals.");
            }
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var now = clock.GetUtcNow().UtcDateTime;
            var lines = b.Lines ?? [];
            var purchase = new Purchase
            {
                SupplierId = b.SupplierId, InvoiceNumber = b.InvoiceNumber.Trim(), InvoiceDate = b.InvoiceDate, ReceivedAt = now, ReceivedBy = user.UserName(),
                DrugsAmount = decimal.Round(lines.Sum(l => l.Quantity * l.UnitCost), 2, MidpointRounding.AwayFromZero), SuppliesAmount = b.SuppliesAmount, SuppliesHst = b.SuppliesHst,
                Lots = lines.Select(l => new StockLot { DrugId = l.DrugId, LotNumber = l.LotNumber.Trim(), ExpiryDate = l.ExpiryDate, QuantityReceived = l.Quantity,
                    QuantityOnHand = l.Quantity, UnitCost = l.UnitCost, ReceivedAt = now }).ToList(),
            };
            db.Purchases.Add(purchase);
            await db.SaveChangesAsync();
            return Results.Created($"/api/purchases/{purchase.PurchaseId}", new { purchase.PurchaseId, purchase.DrugsAmount });
        }).WithTags("Inventory").RequireAuthorization(Policies.Stock);

        api.MapGet("/stock", async (int? drugId, PharmaDbContext db, TimeProvider clock) =>
        {
            var today = PrescriptionEndpoints.Today(clock);
            return await db.StockLots.AsNoTracking().Where(l => l.QuantityOnHand > 0 && (drugId == null || l.DrugId == drugId))
                .OrderBy(l => l.Drug.BrandName).ThenBy(l => l.ExpiryDate)
                .Select(l => new { l.LotId, l.DrugId, drug = l.Drug.BrandName, l.Drug.Din, l.LotNumber, l.ExpiryDate, l.QuantityOnHand, l.UnitCost, expired = l.ExpiryDate <= today,
                    expiresSoon = l.ExpiryDate <= today.AddDays(90) }).ToListAsync();
        }).WithTags("Inventory");

        // Dispense (or refill) a drug that is ready: stock from the lots that expire first, the plan's price, the patient's share.
        api.MapPost("/prescriptions/items/{itemId:int}/dispense", async (int itemId, DispenseBody b, ClaimsPrincipal user, PharmaDbContext db, TimeProvider clock) =>
        {
            var item = await db.PrescriptionItems.Include(i => i.Drug).SingleOrDefaultAsync(i => i.ItemId == itemId);
            if (item is null) return Results.NotFound();
            var rx = await db.Prescriptions.Include(r => r.Plan).SingleAsync(r => r.PrescriptionId == item.PrescriptionId);
            var refill = item.Status == ItemStatus.Dispensed;
            if (!(item.Status == ItemStatus.ReadyToFill || (refill && item.RefillsRemaining > 0)))
                return Results.Problem(refill ? "No refills left: the prescriber must authorize more." : "Only a drug that is ready to fill can be dispensed.", statusCode: 409);
            var quantity = b.Quantity ?? item.Quantity;
            if (quantity <= 0 || quantity > item.Quantity) return Field("quantity", $"Up to the prescribed {item.Quantity}.");
            var today = PrescriptionEndpoints.Today(clock);
            var lots = await db.StockLots.Where(l => l.DrugId == item.DrugId && l.QuantityOnHand > 0 && l.ExpiryDate > today).OrderBy(l => l.ExpiryDate).ThenBy(l => l.LotId).ToListAsync();
            if (lots.Sum(l => l.QuantityOnHand) < quantity)
                return Results.Problem($"Not enough in stock: {lots.Sum(l => l.QuantityOnHand)} on hand that hasn't expired.", statusCode: 409);

            var used = new List<DispenseLot>();
            var left = quantity;
            foreach (var lot in lots)
            {
                if (left == 0) break;
                var take = Math.Min(left, lot.QuantityOnHand);
                lot.QuantityOnHand -= take;
                used.Add(new DispenseLot { LotId = lot.LotId, Quantity = take, UnitCost = lot.UnitCost });
                left -= take;
            }
            var plan = rx.Plan is { IsActive: true } p ? new Pricing.PlanTerms(p.CopayAmount, p.CopayPercent, p.DispensingFee, p.MarkupPercent) : null;
            var price = Pricing.For(used.Sum(u => u.Quantity * u.UnitCost), plan);
            if (price.PatientPays > 0 && b.PaymentMethod is not ("Cash" or "Debit" or "Credit" or "Account"))
                return Field("paymentMethod", $"The patient pays {price.PatientPays:N2}: Cash, Debit, Credit or Account.");

            var dispense = new Dispense
            {
                ItemId = item.ItemId, DrugId = item.DrugId!.Value, IsRefill = refill, Quantity = quantity, DispensedAt = clock.GetUtcNow().UtcDateTime, DispensedBy = user.UserName(),
                PlanId = rx.Plan is { IsActive: true } ? rx.PlanId : null, DrugCost = price.DrugCost, Markup = price.Markup, DispensingFee = price.DispensingFee, Total = price.Total,
                PlanPays = price.PlanPays, PatientPays = price.PatientPays, PaymentMethod = price.PatientPays > 0 ? b.PaymentMethod : null, Lots = used,
            };
            db.Dispenses.Add(dispense);
            if (refill) item.RefillsRemaining--;
            item.Status = ItemStatus.Dispensed;
            await db.SaveChangesAsync();
            var statuses = await db.PrescriptionItems.Where(i => i.PrescriptionId == rx.PrescriptionId).Select(i => i.Status).ToListAsync();
            rx.Status = statuses.Where(s => s != ItemStatus.Cancelled).All(s => s == ItemStatus.Dispensed) ? RxStatus.Completed : RxStatus.InProgress;
            PeopleEndpoints.Audit(db, user, clock, "Update", rx.PatientId, $"Dispensed {item.Drug!.BrandName} on Rx {rx.RxNumber}");
            await db.SaveChangesAsync();
            return Results.Ok(new { dispense.DispenseId, price, monitored = await db.Drugs.AnyAsync(d => d.DrugId == item.DrugId && d.Schedule.Monitored), refillsRemaining = item.RefillsRemaining });
        }).WithTags("Dispensing").RequireAuthorization(Policies.Dispense).AddEndpointFilter(PrescriptionEndpoints.RestrictedFilter);
    }
}
