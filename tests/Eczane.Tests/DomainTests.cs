using Eczane.Domain;
using OnderCode.Kernel.Exceptions;

namespace Eczane.Tests;

internal static class Make
{
    static int _seq;
    public static readonly DateTime T0 = new(2026, 11, 10, 10, 0, 0);
    public static DateOnly Today => DateOnly.FromDateTime(T0);

    public static Product Product(ProductCategory cat = ProductCategory.Rx, decimal price = 100m, PrescriptionColor color = PrescriptionColor.White, bool reimbursed = true)
        => new(Ean13.Complete($"2999{++_seq:00000000}"), $"Ürün {_seq}", cat, price, price * 0.7m, color: color, reimbursed: reimbursed);

    public static StockLot Lot(Product p, int qty, DateOnly expiry, decimal cost = 70m, DateTime? at = null)
        => new(p, $"L{++_seq}", expiry, qty, cost, at ?? T0.AddDays(-30));

    public static Patient Patient(InsuranceType ins = InsuranceType.Sgk, SgkStatus status = SgkStatus.Worker, bool chronic = false)
        => new("Deneme Hasta", 1970, Gender.Female, ins, status, chronic);

    public static Hospital Hospital() => new("Deneme Hastanesi", HospitalKind.State);
}

public sealed class BarcodeAndProductTests
{
    [Theory]
    [InlineData("4006381333931", true)]
    [InlineData("4006381333932", false)]
    [InlineData("400638133393", false)]
    [InlineData("40063813339A1", false)]
    public void Ean13_Validates_CheckDigit(string code, bool valid) => Assert.Equal(valid, Ean13.IsValid(code));

    [Fact]
    public void Ean13_Completes_And_Product_Rejects_Bad_Barcode()
    {
        Assert.True(Ean13.IsValid(Ean13.Complete("200000000001")));
        Assert.Throws<DomainException>(() => new Product("1234567890123", "X", ProductCategory.Otc, 10m, 5m));
    }

    [Fact]
    public void NonRx_Is_Never_Colored_Or_Reimbursed()
    {
        var p = Make.Product(ProductCategory.Otc, color: PrescriptionColor.Red, reimbursed: true);
        Assert.Equal(PrescriptionColor.White, p.Color);
        Assert.False(p.Reimbursed);
        Assert.False(p.RequiresPrescription);
    }
}

public sealed class StockTests
{
    [Fact]
    public void Fefo_Takes_Nearest_Expiry_First_And_Skips_Expired()
    {
        var p = Make.Product();
        var later = Make.Lot(p, 5, Make.Today.AddMonths(6));
        var sooner = Make.Lot(p, 3, Make.Today.AddMonths(2));
        var expired = Make.Lot(p, 10, Make.Today.AddDays(1), at: Make.T0.AddDays(-60));
        var plan = StockRules.Allocate([later, sooner, expired], 6, Make.Today.AddDays(1));
        Assert.Equal(2, plan.Count);
        Assert.Same(sooner, plan[0].Lot);
        Assert.Equal(3, plan[0].Quantity);
        Assert.Same(later, plan[1].Lot);
        Assert.Equal(3, plan[1].Quantity);
        Assert.Throws<DomainException>(() => StockRules.Allocate([later, sooner, expired], 9, Make.Today.AddDays(1)));
    }

    [Theory]
    [InlineData(-1, ExpiryLevel.Expired)]
    [InlineData(0, ExpiryLevel.Expired)]
    [InlineData(1, ExpiryLevel.Critical)]
    [InlineData(30, ExpiryLevel.Critical)]
    [InlineData(31, ExpiryLevel.Watch)]
    [InlineData(90, ExpiryLevel.Watch)]
    [InlineData(91, ExpiryLevel.Ok)]
    public void Expiry_Levels(int days, ExpiryLevel level) => Assert.Equal(level, StockRules.LevelOf(Make.Today.AddDays(days), Make.Today));

    [Theory]
    [InlineData(2.0, 5, 0, 4, 11)]   // hedef 16
    [InlineData(2.0, 20, 0, 4, 0)]
    [InlineData(2.0, 5, 10, 4, 1)]   // yoldaki düşülür
    [InlineData(0.0, 1, 0, 4, 3)]    // satış yok ama en az stok
    public void Reorder_Suggestion(double usage, int onHand, int onOrder, int min, int expected)
        => Assert.Equal(expected, StockRules.SuggestOrder(usage, onHand, onOrder, min, 1, 7));

    [Fact]
    public void Expired_Lot_Cannot_Be_Received_Or_Sold()
    {
        var p = Make.Product();
        Assert.Throws<DomainException>(() => Make.Lot(p, 1, Make.Today, at: Make.T0));
        var lot = Make.Lot(p, 2, Make.Today.AddDays(1));
        Assert.Throws<DomainException>(() => lot.Take(1, Make.Today.AddDays(1)));
        Assert.Throws<DomainException>(() => lot.Take(3, Make.Today));
        Assert.Equal(-1, lot.AdjustTo(1));
    }
}

public sealed class ContributionTests
{
    static decimal Share(InsuranceType ins, SgkStatus s, bool chronic, params (decimal Total, bool Reimbursed, int Boxes)[] lines)
        => ContributionRules.PatientShare(ins, s, chronic, lines);

    [Fact]
    public void Sgk_Box_Fee_Plus_Rate()
    {
        Assert.Equal(33m, Share(InsuranceType.Sgk, SgkStatus.Worker, false, (100m, true, 1), (50m, true, 1)));
        Assert.Equal(18m, Share(InsuranceType.Sgk, SgkStatus.Retired, false, (100m, true, 1), (50m, true, 1)));
        Assert.Equal(3m, Share(InsuranceType.Sgk, SgkStatus.Retired, true, (100m, true, 1), (50m, true, 1)));
        // 5 kutu: 3 ₺ + 2 × 1 ₺
        Assert.Equal(5m + 50m * 0.2m, Share(InsuranceType.Sgk, SgkStatus.Worker, false, (50m, true, 5)));
    }

    [Fact]
    public void NotReimbursed_Paid_By_Patient_And_Other_Insurances()
    {
        Assert.Equal(3m + 20m + 200m, Share(InsuranceType.Sgk, SgkStatus.Worker, false, (100m, true, 1), (200m, false, 1)));
        Assert.Equal(60m, Share(InsuranceType.Private, SgkStatus.Worker, false, (300m, true, 2)));
        Assert.Equal(300m, Share(InsuranceType.None, SgkStatus.Worker, false, (300m, true, 2)));
        // Pay hiçbir zaman toplamı geçmez
        Assert.Equal(2m, Share(InsuranceType.Sgk, SgkStatus.Worker, false, (2m, true, 1)));
    }
}

public sealed class SaleTests
{
    [Fact]
    public void Prescription_Completes_With_Shares_And_Return_Restores_Stock()
    {
        var p = Make.Product(price: 100m);
        var lot = Make.Lot(p, 10, Make.Today.AddMonths(6));
        var sale = Sale.Prescription("S1", Make.T0, Make.Patient(), Make.Hospital(), "Kardiyoloji", PrescriptionColor.White, "ABC1234", Make.Today);
        sale.AddLine(p, lot, 2, Make.Today);
        sale.Complete(PaymentType.Card);
        Assert.Equal(200m, sale.Total);
        Assert.Equal(3m + 40m, sale.PatientShare);
        Assert.Equal(157m, sale.InstitutionShare);
        Assert.Equal(8, lot.Quantity);
        Assert.Equal(140m, sale.Cost);
        sale.Return(Make.T0.AddHours(1));
        Assert.Equal(10, lot.Quantity);
        Assert.Equal(SaleStatus.Returned, sale.Status);
        Assert.Throws<DomainException>(() => sale.Return(Make.T0));
    }

    [Fact]
    public void Rx_Rules()
    {
        var rx = Make.Product();
        var green = Make.Product(color: PrescriptionColor.Green);
        var lot = Make.Lot(rx, 5, Make.Today.AddMonths(3));
        var greenLot = Make.Lot(green, 5, Make.Today.AddMonths(3));
        var retail = Sale.Retail("R1", Make.T0);
        Assert.Throws<DomainException>(() => retail.AddLine(rx, lot, 1, Make.Today));
        var white = Sale.Prescription("S2", Make.T0, Make.Patient(), Make.Hospital(), "Psikiyatri", PrescriptionColor.White, "X1", Make.Today);
        Assert.Throws<DomainException>(() => white.AddLine(green, greenLot, 1, Make.Today));
        var greenRx = Sale.Prescription("S3", Make.T0, Make.Patient(), Make.Hospital(), "Psikiyatri", PrescriptionColor.Green, "X2", Make.Today);
        greenRx.AddLine(green, greenLot, 1, Make.Today);
        greenRx.AddLine(rx, lot, 1, Make.Today);
        Assert.Equal(2, greenRx.Lines.Count);
        // Süresi dolmuş reçete: renkli reçete 2 gün
        Assert.Throws<DomainException>(() => Sale.Prescription("S4", Make.T0, Make.Patient(), Make.Hospital(), "Psikiyatri", PrescriptionColor.Green, "X3", Make.Today.AddDays(-3)));
        Assert.Throws<DomainException>(() => Sale.Prescription("S5", Make.T0, Make.Patient(), Make.Hospital(), "Kardiyoloji", PrescriptionColor.White, "X4", Make.Today.AddDays(1)));
    }

    [Fact]
    public void Retail_Discount_Is_Capped()
    {
        var p = Make.Product(ProductCategory.Dermo, 100m);
        var lot = Make.Lot(p, 5, Make.Today.AddMonths(3));
        var s = Sale.Retail("R2", Make.T0);
        s.AddLine(p, lot, 1, Make.Today);
        Assert.Throws<DomainException>(() => s.Complete(PaymentType.Cash, 20m));
        s.Complete(PaymentType.Cash, 10m);
        Assert.Equal(90m, s.PatientShare);
        Assert.Equal(0m, s.InstitutionShare);
    }
}

public sealed class PurchasingTests
{
    [Fact]
    public void Order_Merges_Lines_And_Receive_Creates_Lots_With_Free_Goods()
    {
        var w = new Wholesaler("Depo", 2, 30, 0.1m);
        var p = Make.Product(price: 110m);
        var o = new PurchaseOrder("SP-1", w, Make.T0);
        Assert.Throws<DomainException>(() => o.Place(Make.T0));
        o.AddLine(p, 6, 50m);
        o.AddLine(p, 4, 50m, 1);
        Assert.Single(o.Lines);
        Assert.Equal(500m, o.Total);
        o.Place(Make.T0);
        Assert.Throws<DomainException>(() => o.AddLine(p, 1, 50m));
        var lots = o.Receive(Make.T0.AddDays(1), _ => ("LOT1", Make.Today.AddYears(2)));
        var lot = Assert.Single(lots);
        Assert.Equal(11, lot.Quantity);
        Assert.Equal(Math.Round(500m / 11, 4), lot.UnitCost);
        Assert.Equal(Make.Today.AddDays(31), o.DueDate);
        Assert.Equal(Math.Round(500m / 11, 2), p.PurchasePrice);
        Assert.Throws<DomainException>(() => o.Cancel());
    }

    [Fact]
    public void Free_Goods_Campaign()
    {
        Assert.Equal(2, FreeGoods.For(25, 10, 1));
        var c = new FreeGoodsCampaign(new Wholesaler("Depo", 1, 30, 0.1m), Make.Product(), 10, 1, Make.Today.AddDays(10));
        Assert.Equal(20, c.RoundUp(18));
        Assert.Equal(12, c.RoundUp(12));
        Assert.Equal(1, c.FreeFor(14));
        Assert.True(c.IsValid(Make.Today));
        Assert.False(c.IsValid(Make.Today.AddDays(11)));
    }
}

public sealed class StaffDutyAssetTests
{
    [Fact]
    public void Leave_Entitlement_And_WorkDays()
    {
        var e = new Employee("A", StaffRole.Technician, new DateOnly(2020, 3, 16), 1m);
        Assert.Equal(20, e.AnnualLeaveEntitlement(new DateOnly(2026, 11, 12)));
        Assert.Equal(14, e.AnnualLeaveEntitlement(new DateOnly(2021, 3, 16)));
        Assert.Equal(0, e.AnnualLeaveEntitlement(new DateOnly(2021, 3, 15)));
        Assert.Equal(26, new Employee("B", StaffRole.Pharmacist, new DateOnly(2000, 1, 1), 1m).AnnualLeaveEntitlement(new DateOnly(2026, 1, 1)));
        // 9-15 Kasım 2026: pazartesi-pazar, pazar hariç 6 gün
        Assert.Equal(6, LeaveRecord.WorkDays(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void Duty_Planner_Is_Fair_Respects_Leave_And_Rest()
    {
        var people = new List<DutyPlanner.Person> { new(1, "E1", true), new(2, "E2", true), new(3, "E3", true), new(4, "Y1", false), new(5, "Y2", false) };
        var start = new DateOnly(2026, 12, 1);
        var shifts = Enumerable.Range(0, 12).Select(i => (start.AddDays(i * 3), i % 4 == 3 ? DutyKind.Sunday : DutyKind.Night)).ToList();
        var plan = DutyPlanner.Plan(shifts, people, (id, d) => id == 2 && d < start.AddDays(10));
        Assert.All(plan, a => Assert.NotNull(a.PharmacistId));
        Assert.DoesNotContain(plan, a => a.PharmacistId == 2 && a.Date < start.AddDays(10));
        var points = plan.GroupBy(a => a.PharmacistId).ToDictionary(g => g.Key!.Value, g => g.Sum(a => DutyPlanner.PointsOf(a.Kind)));
        Assert.True(points.Values.Max() - points.Values.Min() <= 3);
        // Kimse art arda (2 gün içinde) iki nöbet tutmaz
        foreach (var g in plan.GroupBy(a => a.PharmacistId))
        {
            var days = g.Select(a => a.Date).OrderBy(d => d).ToList();
            for (var i = 1; i < days.Count; i++) Assert.True(days[i].DayNumber - days[i - 1].DayNumber > DutyPlanner.RestDays);
        }
        var asst = plan.GroupBy(a => a.AssistantId).Select(g => g.Sum(a => DutyPlanner.PointsOf(a.Kind))).ToList();
        Assert.True(asst.Max() - asst.Min() <= 2);
    }

    [Fact]
    public void Duty_Requires_A_Pharmacist()
    {
        var s = new DutyShift(new DateOnly(2026, 12, 3), DutyKind.Night);
        Assert.Throws<DomainException>(() => s.Assign(new Employee("T", StaffRole.Technician, new DateOnly(2020, 1, 1), 1m), null));
        s.Assign(new Employee("E", StaffRole.Pharmacist, new DateOnly(2020, 1, 1), 1m), null);
        Assert.True(s.IsStaffed);
        Assert.Equal(1, s.Points);
    }

    [Fact]
    public void Straight_Line_Depreciation()
    {
        var a = new Asset("DM-1", "Dolap", AssetCategory.ColdChain, new DateOnly(2024, 1, 15), 12_000m, 5, "Arka", 0m, 6);
        Assert.Equal(200m, a.MonthlyDepreciation);
        Assert.Equal(2_400m, a.AccumulatedDepreciation(new DateOnly(2024, 12, 31)));
        Assert.Equal(2_400m, a.DepreciationInYear(2025));
        Assert.Equal(12_000m, a.AccumulatedDepreciation(new DateOnly(2030, 1, 1)));
        Assert.Equal(0m, a.BookValue(new DateOnly(2030, 1, 1)));
        Assert.Equal(new DateOnly(2024, 7, 15), a.NextMaintenance);
        _ = new MaintenanceRecord(a, new DateOnly(2024, 7, 20), MaintenanceKind.Maintenance, 500m);
        Assert.Equal(new DateOnly(2025, 1, 20), a.NextMaintenance);
        a.Retire(new DateOnly(2025, 1, 31));
        Assert.Equal(a.AccumulatedDepreciation(new DateOnly(2025, 1, 31)), a.AccumulatedDepreciation(new DateOnly(2027, 1, 1)));
        Assert.Null(a.NextMaintenance);
        Assert.Throws<DomainException>(() => new MaintenanceRecord(a, new DateOnly(2025, 2, 1), MaintenanceKind.Repair, 1m));
    }
}
