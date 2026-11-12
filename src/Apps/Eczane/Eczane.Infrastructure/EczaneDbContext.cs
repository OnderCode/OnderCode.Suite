using Eczane.Application;
using Eczane.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Data;

namespace Eczane.Infrastructure;

public sealed class EczaneDbContext(DbContextOptions<EczaneDbContext> options) : SuiteDbContext(options)
{
    public DbSet<Employee> Employees { get; set; } = null!;
    public DbSet<LeaveRecord> Leaves { get; set; } = null!;
    public DbSet<DutyShift> DutyShifts { get; set; } = null!;
    public DbSet<Asset> Assets { get; set; } = null!;
    public DbSet<MaintenanceRecord> Maintenance { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<StockLot> Lots { get; set; } = null!;
    public DbSet<StockMovement> Movements { get; set; } = null!;
    public DbSet<Wholesaler> Wholesalers { get; set; } = null!;
    public DbSet<FreeGoodsCampaign> Campaigns { get; set; } = null!;
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
    public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; } = null!;
    public DbSet<SupplierPayment> SupplierPayments { get; set; } = null!;
    public DbSet<Patient> Patients { get; set; } = null!;
    public DbSet<Hospital> Hospitals { get; set; } = null!;
    public DbSet<Sale> Sales { get; set; } = null!;
    public DbSet<SaleLine> SaleLines { get; set; } = null!;
    public DbSet<InstitutionInvoice> Invoices { get; set; } = null!;
}

public static class EczaneInfrastructure
{
    /// <summary>Veri katmanı + uygulama katmanı + örnek veri. Arayüz yalnızca bunu çağırır.</summary>
    public static IServiceCollection AddEczane(this IServiceCollection services, Action<SuiteDatabaseOptions>? configure = null)
    {
        services.AddSuiteData<EczaneDbContext>(o =>
        {
            o.AppName = "Eczane";
            configure?.Invoke(o);
        });
        services.AddSeeder<EczaneDbContext, EczaneDemoSeeder>();
        services.AddEczaneApplication();
        return services;
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b) => b.Property(x => x.FullName).HasMaxLength(100);
}

internal sealed class LeaveConfiguration : IEntityTypeConfiguration<LeaveRecord>
{
    public void Configure(EntityTypeBuilder<LeaveRecord> b)
    {
        b.Property(x => x.Note).HasMaxLength(150);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId);
        b.HasIndex(x => new { x.EmployeeId, x.Start });
    }
}

internal sealed class DutyConfiguration : IEntityTypeConfiguration<DutyShift>
{
    public void Configure(EntityTypeBuilder<DutyShift> b)
    {
        b.Property(x => x.Note).HasMaxLength(150);
        b.HasOne(x => x.Pharmacist).WithMany().HasForeignKey(x => x.PharmacistId);
        b.HasOne(x => x.Assistant).WithMany().HasForeignKey(x => x.AssistantId);
        b.HasIndex(x => x.Date).IsUnique();
    }
}

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> b)
    {
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Location).HasMaxLength(60);
        b.Property(x => x.SerialNo).HasMaxLength(60);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class MaintenanceConfiguration : IEntityTypeConfiguration<MaintenanceRecord>
{
    public void Configure(EntityTypeBuilder<MaintenanceRecord> b)
    {
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Barcode).HasMaxLength(13);
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.ActiveIngredient).HasMaxLength(120);
        b.Property(x => x.Group).HasMaxLength(60);
        b.Property(x => x.Shelf).HasMaxLength(12);
        b.HasIndex(x => x.Barcode).IsUnique();
        b.HasIndex(x => x.Name);
    }
}

internal sealed class LotConfiguration : IEntityTypeConfiguration<StockLot>
{
    public void Configure(EntityTypeBuilder<StockLot> b)
    {
        b.Property(x => x.LotNo).HasMaxLength(30);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId);
        b.HasIndex(x => new { x.ProductId, x.Expiry });
        b.HasIndex(x => x.Expiry);
    }
}

internal sealed class MovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.Property(x => x.Reference).HasMaxLength(150);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId);
        b.HasIndex(x => new { x.ProductId, x.At });
    }
}

internal sealed class WholesalerConfiguration : IEntityTypeConfiguration<Wholesaler>
{
    public void Configure(EntityTypeBuilder<Wholesaler> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<FreeGoodsCampaign>
{
    public void Configure(EntityTypeBuilder<FreeGoodsCampaign> b)
    {
        b.HasOne(x => x.Wholesaler).WithMany().HasForeignKey(x => x.WholesalerId);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.HasIndex(x => new { x.WholesalerId, x.ProductId });
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.Property(x => x.Number).HasMaxLength(20);
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasOne(x => x.Wholesaler).WithMany().HasForeignKey(x => x.WholesalerId);
        b.HasMany(x => x.Lines).WithOne(l => l.Order).HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => new { x.Status, x.ReceivedAt });
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.Property(x => x.LotNo).HasMaxLength(30);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
    }
}

internal sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> b)
    {
        b.Property(x => x.Note).HasMaxLength(150);
        b.HasOne(x => x.Wholesaler).WithMany().HasForeignKey(x => x.WholesalerId);
    }
}

internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> b)
    {
        b.Property(x => x.FullName).HasMaxLength(100);
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasIndex(x => x.FullName);
    }
}

internal sealed class HospitalConfiguration : IEntityTypeConfiguration<Hospital>
{
    public void Configure(EntityTypeBuilder<Hospital> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.Property(x => x.Number).HasMaxLength(20);
        b.Property(x => x.Branch).HasMaxLength(60);
        b.Property(x => x.PrescriptionNo).HasMaxLength(20);
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId);
        b.HasOne(x => x.Hospital).WithMany().HasForeignKey(x => x.HospitalId);
        b.HasOne(x => x.SoldBy).WithMany().HasForeignKey(x => x.SoldById);
        b.HasMany(x => x.Lines).WithOne(l => l.Sale).HasForeignKey(l => l.SaleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => x.SoldAt);
        b.HasIndex(x => new { x.HospitalId, x.SoldAt });
        b.HasIndex(x => new { x.PatientId, x.SoldAt });
    }
}

internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> b)
    {
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId);
        b.HasIndex(x => x.ProductId);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<InstitutionInvoice>
{
    public void Configure(EntityTypeBuilder<InstitutionInvoice> b)
        => b.HasIndex(x => new { x.Institution, x.Year, x.Month }).IsUnique();
}
