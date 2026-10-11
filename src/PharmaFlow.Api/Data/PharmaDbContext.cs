using Microsoft.EntityFrameworkCore;

namespace PharmaFlow.Api.Data;

/// <summary>Maps the tables built by database/*.sql. No migrations: the SQL scripts own the schema.</summary>
public class PharmaDbContext(DbContextOptions<PharmaDbContext> options) : DbContext(options)
{
    public DbSet<DosageForm> DosageForms => Set<DosageForm>();
    public DbSet<RouteOfAdmin> Routes => Set<RouteOfAdmin>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<SigCode> SigCodes => Set<SigCode>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Interaction> Interactions => Set<Interaction>();
    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Drug> Drugs => Set<Drug>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientAllergy> Allergies => Set<PatientAllergy>();
    public DbSet<Prescriber> Prescribers => Set<Prescriber>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<AccessLog> AccessLog => Set<AccessLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<DosageForm>(e => { e.ToTable("DosageForm", "ref"); e.HasKey(x => x.Code); });
        b.Entity<RouteOfAdmin>(e => { e.ToTable("Route", "ref"); e.HasKey(x => x.Code); });
        b.Entity<Unit>(e => { e.ToTable("Unit", "ref"); e.HasKey(x => x.Code); });
        b.Entity<Schedule>(e => { e.ToTable("Schedule", "ref"); e.HasKey(x => x.Code); });
        b.Entity<SigCode>(e => { e.ToTable("SigCode", "ref"); e.HasKey(x => x.Code); e.Property(x => x.DosesPerDay).HasPrecision(5, 2); });
        b.Entity<Ingredient>().ToTable("Ingredient", "ref");
        b.Entity<Interaction>(e =>
        {
            e.ToTable("Interaction", "ref"); e.HasKey(x => new { x.IngredientAId, x.IngredientBId });
            e.HasOne(x => x.IngredientA).WithMany().HasForeignKey(x => x.IngredientAId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.IngredientB).WithMany().HasForeignKey(x => x.IngredientBId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Manufacturer>().ToTable("Manufacturer", "ref");
        b.Entity<Supplier>().ToTable("Supplier", "ref");
        b.Entity<Plan>(e =>
        {
            e.ToTable("Plan", "ref");
            e.Property(x => x.CopayAmount).HasPrecision(8, 2); e.Property(x => x.DispensingFee).HasPrecision(8, 2);
            e.Property(x => x.CopayPercent).HasPrecision(5, 2); e.Property(x => x.MarkupPercent).HasPrecision(5, 2);
        });
        b.Entity<Drug>(e =>
        {
            e.ToTable("Drug", "ref");
            e.Property(x => x.Din).HasColumnType("char(8)");
            e.Property(x => x.Strength).HasPrecision(10, 3);
            e.Property(x => x.UnitCost).HasPrecision(10, 4);
            e.HasOne(x => x.Schedule).WithMany().HasForeignKey(x => x.ScheduleCode);
        });
        b.Entity<Patient>(e =>
        {
            e.ToTable("Patient", "core");
            e.Property(x => x.HealthCardNumber).HasColumnType("char(10)");
            e.Property(x => x.Sex).HasColumnType("char(1)");
            e.Property(x => x.RowVer).IsRowVersion();
            e.HasMany(x => x.Allergies).WithOne().HasForeignKey(x => x.PatientId);
        });
        b.Entity<PatientAllergy>(e => { e.ToTable("PatientAllergy", "core"); e.HasKey(x => x.AllergyId); });
        b.Entity<Prescriber>().ToTable("Prescriber", "core");
        b.Entity<Prescription>(e =>
        {
            e.ToTable("Prescription", "rx");
            e.Property(x => x.RowVer).IsRowVersion();
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PrescriptionId);
        });
        b.Entity<PrescriptionItem>(e => { e.ToTable("PrescriptionItem", "rx"); e.HasKey(x => x.ItemId); e.Property(x => x.Quantity).HasPrecision(10, 2); });
        b.Entity<AccessLog>(e => { e.ToTable("AccessLog", "audit"); e.HasKey(x => x.AuditId); });
    }

    public async Task<int> NextRxNumberAsync() =>
        (await Database.SqlQueryRaw<int>("SELECT NEXT VALUE FOR rx.RxNumber AS Value").ToListAsync()).Single();
}
