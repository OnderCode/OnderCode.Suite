using Eczane.Domain;
using FluentValidation;
using OnderCode.Analytics;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace Eczane.Application;

public sealed record ReportPeriod(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
    public ReportPeriod Previous => new(From.AddDays(-Days), From.AddDays(-1));
}

public sealed class PeriodRules : AbstractValidator<ReportPeriod>
{
    public PeriodRules()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("Bitiş tarihi başlangıçtan önce olamaz.");
        RuleFor(x => x).Must(p => p.Days <= 366).WithMessage("Dönem en fazla 366 gün olabilir.");
    }
}

/// <summary>Raporlarda kullanılan satış satırı verisi: tek sorguda çekilir, hesaplar bellekte yapılır.</summary>
internal sealed record LineFact(int SaleId, DateTime At, SaleKind Kind, int? HospitalId, string? Hospital, HospitalKind? HospitalKind, string? Branch,
    PrescriptionColor? Color, InsuranceType Insurance, PaymentType? Payment, int? SoldById, int ProductId, string Product, ProductCategory Category,
    string? Group, int Quantity, decimal UnitPrice, decimal UnitCost, decimal SalePatientShare, decimal SaleInstitutionShare, decimal SaleTotal, decimal SaleDiscount)
{
    public decimal Total => Quantity * UnitPrice;
    public decimal Cost => Quantity * UnitCost;

    /// <summary>Satırın net ciroya düşen payı (indirim satış toplamına oranla dağıtılır).</summary>
    public decimal Net => SaleTotal == 0 ? 0 : Total * (SalePatientShare + SaleInstitutionShare) / SaleTotal;
}

internal static class Facts
{
    public static async Task<List<LineFact>> LoadAsync(IUnitOfWork uow, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var start = from.Start();
        var end = to.AddDays(1).Start();
        var list = await uow.Repository<SaleLine>().ListAsync(
            new Specification<SaleLine>().Where(l => l.Sale.SoldAt >= start && l.Sale.SoldAt < end && l.Sale.Status == SaleStatus.Completed),
            l => new LineFact(l.SaleId, l.Sale.SoldAt, l.Sale.Kind, l.Sale.HospitalId, l.Sale.Hospital!.Name, (HospitalKind?)l.Sale.Hospital!.Kind, l.Sale.Branch,
                l.Sale.Color, l.Sale.Insurance, l.Sale.Payment, l.Sale.SoldById, l.ProductId, l.Product.Name, l.Product.Category, l.Product.Group,
                l.Quantity, l.UnitPrice, l.UnitCost, l.Sale.PatientShare, l.Sale.InstitutionShare, l.Sale.Total, l.Sale.Discount), ct);
        return list.ToList();
    }
}

// ---------- 1. Satış raporu ----------

public sealed record SalesDay(DateOnly Date, int Prescriptions, int Retail, decimal RxRevenue, decimal RetailRevenue, decimal PatientShare, decimal InstitutionShare,
    decimal Profit)
{
    public string DayName => Texts.DayName(Date);
    public decimal Total => RxRevenue + RetailRevenue;
    public int Count => Prescriptions + Retail;
}

public sealed record CategoryRow(string Category, int Boxes, decimal Revenue, decimal Profit, double Share)
{
    public double Margin => Revenue == 0 ? 0 : (double)(Profit / Revenue);
}

public sealed record PaymentRow(string Method, int Count, decimal Amount, double Share);

public sealed record SellerRow(string Name, int Sales, decimal Revenue, decimal AverageBasket);

public sealed record SalesReport(DateOnly From, DateOnly To, IReadOnlyList<SalesDay> Days, IReadOnlyList<CategoryRow> Categories, IReadOnlyList<PaymentRow> Payments,
    IReadOnlyList<SellerRow> Sellers, decimal PreviousTotal, decimal PreviousProfit, int PreviousCount, IReadOnlyList<CategoryRow> Insurances)
{
    public decimal Total => Days.Sum(d => d.Total);
    public decimal Profit => Days.Sum(d => d.Profit);
    public int Count => Days.Sum(d => d.Count);
    public int Prescriptions => Days.Sum(d => d.Prescriptions);
    public decimal Rx => Days.Sum(d => d.RxRevenue);
    public decimal Retail => Days.Sum(d => d.RetailRevenue);
    public decimal Institution => Days.Sum(d => d.InstitutionShare);
    public double Margin => Total == 0 ? 0 : (double)(Profit / Total);
    public decimal AverageBasket => Count == 0 ? 0 : Total / Count;
}

public sealed record GetSalesReport(DateOnly From, DateOnly To) : IQuery<SalesReport>;

public sealed class GetSalesReportValidator : AbstractValidator<GetSalesReport>
{
    public GetSalesReportValidator() => RuleFor(x => new ReportPeriod(x.From, x.To)).SetValidator(new PeriodRules());
}

public sealed class GetSalesReportHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetSalesReport, SalesReport>
{
    public async Task<Result<SalesReport>> HandleAsync(GetSalesReport q, CancellationToken cancellationToken)
    {
        var period = new ReportPeriod(q.From, q.To);
        await using var uow = uowFactory.Create();
        var facts = await Facts.LoadAsync(uow, period.Previous.From, q.To, cancellationToken);
        var cur = facts.Where(f => DateOnly.FromDateTime(f.At) >= q.From).ToList();
        var prev = facts.Where(f => DateOnly.FromDateTime(f.At) < q.From).ToList();
        var sales = cur.GroupBy(f => f.SaleId).Select(g => g.First()).ToList();
        var employees = (await uow.Repository<Employee>().ListAsync(new Specification<Employee>().WithDeleted(), e => new { e.Id, e.FullName }, cancellationToken))
            .ToDictionary(e => e.Id, e => e.FullName);

        var days = Enumerable.Range(0, period.Days).Select(i => q.From.AddDays(i)).Select(d =>
        {
            var daySales = sales.Where(s => DateOnly.FromDateTime(s.At) == d).ToList();
            var dayLines = cur.Where(f => DateOnly.FromDateTime(f.At) == d).ToList();
            return new SalesDay(d, daySales.Count(s => s.Kind == SaleKind.Prescription), daySales.Count(s => s.Kind == SaleKind.Retail),
                daySales.Where(s => s.Kind == SaleKind.Prescription).Sum(s => s.SalePatientShare + s.SaleInstitutionShare),
                daySales.Where(s => s.Kind == SaleKind.Retail).Sum(s => s.SalePatientShare),
                daySales.Sum(s => s.SalePatientShare), daySales.Sum(s => s.SaleInstitutionShare),
                daySales.Sum(s => s.SalePatientShare + s.SaleInstitutionShare) - dayLines.Sum(f => f.Cost));
        }).ToList();
        var total = cur.Sum(f => f.Net);
        var categories = cur.GroupBy(f => f.Category).Select(g => new CategoryRow(Texts.Of(g.Key), g.Sum(f => f.Quantity), g.Sum(f => f.Net),
            g.Sum(f => f.Net - f.Cost), total == 0 ? 0 : (double)(g.Sum(f => f.Net) / total))).OrderByDescending(c => c.Revenue).ToList();
        var collected = sales.Sum(s => s.SalePatientShare);
        var payments = sales.Where(s => s.Payment is not null).GroupBy(s => s.Payment!.Value)
            .Select(g => new PaymentRow(Texts.Of(g.Key), g.Count(), g.Sum(s => s.SalePatientShare), collected == 0 ? 0 : (double)(g.Sum(s => s.SalePatientShare) / collected)))
            .Append(new PaymentRow("Kurum payı (fatura)", sales.Count(s => s.SaleInstitutionShare > 0), sales.Sum(s => s.SaleInstitutionShare), 0))
            .ToList();
        var sellers = sales.Where(s => s.SoldById is not null).GroupBy(s => s.SoldById!.Value).Select(g =>
        {
            var revenue = g.Sum(s => s.SalePatientShare + s.SaleInstitutionShare);
            return new SellerRow(employees.GetValueOrDefault(g.Key) ?? "?", g.Count(), revenue, g.Count() == 0 ? 0 : revenue / g.Count());
        }).OrderByDescending(s => s.Revenue).ToList();
        var rxSales = sales.Where(s => s.Kind == SaleKind.Prescription).ToList();
        var rxTotal = rxSales.Sum(s => s.SalePatientShare + s.SaleInstitutionShare);
        var insurances = rxSales.GroupBy(s => s.Insurance).Select(g => new CategoryRow(Texts.Of(g.Key), g.Count(),
            g.Sum(s => s.SalePatientShare + s.SaleInstitutionShare), g.Sum(s => s.SaleInstitutionShare),
            rxTotal == 0 ? 0 : (double)(g.Sum(s => s.SalePatientShare + s.SaleInstitutionShare) / rxTotal))).OrderByDescending(r => r.Revenue).ToList();
        var prevSales = prev.GroupBy(f => f.SaleId).Select(g => g.First()).ToList();
        return new SalesReport(q.From, q.To, days, categories, payments, sellers,
            prevSales.Sum(s => s.SalePatientShare + s.SaleInstitutionShare), prev.Sum(f => f.Net - f.Cost), prevSales.Count, insurances);
    }
}

// ---------- 2. Hastane ve branş analizi ----------

public sealed record HospitalShare(string Hospital, string Kind, int Prescriptions, int Patients, decimal Revenue, double Share, double Cumulative, string Class,
    int PreviousPrescriptions, string? TopBranch)
{
    public double? Growth => PreviousPrescriptions == 0 ? null : (Prescriptions - PreviousPrescriptions) / (double)PreviousPrescriptions;
}

public sealed record GroupShare(string Name, int Prescriptions, decimal Revenue, double Share);

public sealed record HospitalReport(DateOnly From, DateOnly To, IReadOnlyList<HospitalShare> Hospitals, IReadOnlyList<GroupShare> Kinds,
    IReadOnlyList<GroupShare> Branches, IReadOnlyList<GroupShare> Colors, string[] WeekLabels, IReadOnlyList<(string Name, double[] Values)> TopTrend,
    int Prescriptions, int PreviousPrescriptions, int Patients, double TopThreeShare, IReadOnlyList<GroupShare> Groups)
{
    public string? Leader => Hospitals.FirstOrDefault()?.Hospital;
}

/// <summary>Reçeteler en çok hangi kurumdan, hangi branştan geliyor? Pareto (ABC), kurum türü, branş, reçete rengi ve haftalık eğilim.</summary>
public sealed record GetHospitalReport(DateOnly From, DateOnly To) : IQuery<HospitalReport>;

public sealed class GetHospitalReportValidator : AbstractValidator<GetHospitalReport>
{
    public GetHospitalReportValidator() => RuleFor(x => new ReportPeriod(x.From, x.To)).SetValidator(new PeriodRules());
}

public sealed class GetHospitalReportHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetHospitalReport, HospitalReport>
{
    public async Task<Result<HospitalReport>> HandleAsync(GetHospitalReport q, CancellationToken cancellationToken)
    {
        var period = new ReportPeriod(q.From, q.To);
        await using var uow = uowFactory.Create();
        var start = period.Previous.From.Start();
        var end = q.To.AddDays(1).Start();
        var rx = await uow.Repository<Sale>().ListAsync(new Specification<Sale>().Where(s => s.Kind == SaleKind.Prescription && s.Status == SaleStatus.Completed
                && s.SoldAt >= start && s.SoldAt < end),
            s => new { s.Id, s.SoldAt, s.PatientId, Hospital = s.Hospital!.Name, s.Hospital!.Kind, s.Branch, s.Color, s.PatientShare, s.InstitutionShare }, cancellationToken);
        var cur = rx.Where(s => DateOnly.FromDateTime(s.SoldAt) >= q.From).ToList();
        var prev = rx.Where(s => DateOnly.FromDateTime(s.SoldAt) < q.From).ToList();
        var count = cur.Count;
        var byHospital = cur.GroupBy(s => s.Hospital).Select(g => new
        {
            Name = g.Key, Kind = Texts.Of(g.First().Kind), Count = g.Count(), Patients = g.Select(s => s.PatientId).Distinct().Count(),
            Revenue = g.Sum(s => s.PatientShare + s.InstitutionShare),
            Branch = g.GroupBy(s => s.Branch).OrderByDescending(b => b.Count()).First().Key,
        }).ToList();
        var pareto = Pareto.Analyze(byHospital, h => h.Count);
        var hospitals = pareto.Items.Select(i => new HospitalShare(i.Item.Name, i.Item.Kind, i.Item.Count, i.Item.Patients, i.Item.Revenue, i.Share, i.Cumulative,
            i.Class.ToString(), prev.Count(p => p.Hospital == i.Item.Name), i.Item.Branch)).ToList();

        List<GroupShare> Share(IEnumerable<IGrouping<string, (int Id, decimal Revenue)>> groups)
            => groups.Select(g => new GroupShare(g.Key, g.Count(), g.Sum(x => x.Revenue), count == 0 ? 0 : g.Count() / (double)count))
                .OrderByDescending(x => x.Prescriptions).ToList();

        var kinds = Share(cur.GroupBy(s => Texts.Of(s.Kind), s => (Id: s.Id, Revenue: s.PatientShare + s.InstitutionShare)));
        var branches = Share(cur.GroupBy(s => s.Branch ?? "Belirtilmemiş", s => (Id: s.Id, Revenue: s.PatientShare + s.InstitutionShare)));
        var colors = Share(cur.GroupBy(s => s.Color is { } c ? Texts.Of(c) : "Beyaz", s => (Id: s.Id, Revenue: s.PatientShare + s.InstitutionShare)));

        // Haftalık eğilim: ilk 5 kurum
        var weeks = Math.Max(1, (int)Math.Ceiling(period.Days / 7.0));
        var weekLabels = Enumerable.Range(0, weeks).Select(w => q.From.AddDays(w * 7).ToString("dd.MM")).ToArray();
        var trend = hospitals.Take(5).Select(h =>
        {
            var values = new double[weeks];
            foreach (var s in cur.Where(s => s.Hospital == h.Hospital))
            {
                var w = (DateOnly.FromDateTime(s.SoldAt).DayNumber - q.From.DayNumber) / 7;
                if (w >= 0 && w < weeks) values[w]++;
            }
            return (h.Hospital, values);
        }).ToList();

        // Tedavi grupları (reçete satırlarından)
        var lines = await uow.Repository<SaleLine>().ListAsync(new Specification<SaleLine>().Where(l => l.Sale.Kind == SaleKind.Prescription && l.Sale.Status == SaleStatus.Completed
                && l.Sale.SoldAt >= period.From.Start() && l.Sale.SoldAt < end),
            l => new { l.SaleId, l.Product.Group, l.Quantity, l.UnitPrice }, cancellationToken);
        var groupTotal = lines.Sum(l => l.Quantity * l.UnitPrice);
        var groups = lines.GroupBy(l => l.Group ?? "Diğer").Select(g => new GroupShare(g.Key, g.Select(l => l.SaleId).Distinct().Count(), g.Sum(l => l.Quantity * l.UnitPrice),
            groupTotal == 0 ? 0 : (double)(g.Sum(l => l.Quantity * l.UnitPrice) / groupTotal))).OrderByDescending(g => g.Revenue).ToList();

        return new HospitalReport(q.From, q.To, hospitals, kinds, branches, colors, weekLabels, trend, count, prev.Count,
            cur.Select(s => s.PatientId).Distinct().Count(), hospitals.Take(3).Sum(h => h.Share), groups);
    }
}

// ---------- 3. Ürün ABC ve stok devri ----------

public sealed record ProductShare(string Product, string Category, string? Group, int Boxes, decimal Revenue, decimal Profit, double Share, double Cumulative,
    string Class, int Stock, decimal StockValue)
{
    public double Margin => Revenue == 0 ? 0 : (double)(Profit / Revenue);
}

public sealed record SlowMover(string Product, string Category, int Stock, decimal StockValue, DateTime? LastSale, DateOnly? NearestExpiry);

public sealed record StockValueRow(string Category, int Products, int Boxes, decimal Value, double Share);

public sealed record ProductReport(DateOnly From, DateOnly To, IReadOnlyList<ProductShare> Products, IReadOnlyList<SlowMover> SlowMovers,
    IReadOnlyList<StockValueRow> StockByCategory, int ACount, double AShareOfRevenue, double ACountShare, decimal StockValue, decimal CostOfSales)
{
    /// <summary>Stok devir hızı (yıllık): satılan malın maliyeti / ortalama stok değeri, döneme göre yıllaştırılmış.</summary>
    public double Turnover => StockValue == 0 ? 0 : (double)(CostOfSales / StockValue) * 365.0 / Math.Max(1, To.DayNumber - From.DayNumber + 1);

    public int DaysOfInventory => Turnover <= 0 ? 0 : (int)Math.Round(365 / Turnover);
}

public sealed record GetProductReport(DateOnly From, DateOnly To) : IQuery<ProductReport>;

public sealed class GetProductReportValidator : AbstractValidator<GetProductReport>
{
    public GetProductReportValidator() => RuleFor(x => new ReportPeriod(x.From, x.To)).SetValidator(new PeriodRules());
}

public sealed class GetProductReportHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetProductReport, ProductReport>
{
    public async Task<Result<ProductReport>> HandleAsync(GetProductReport q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var facts = await Facts.LoadAsync(uow, q.From, q.To, cancellationToken);
        var stock = await StockQueries.OnHandAsync(uow, today, cancellationToken);
        var products = await uow.Repository<Product>().ListAsync(new Specification<Product>().Where(p => p.IsActive),
            p => new { p.Id, p.Name, p.Category, p.Group }, cancellationToken);
        var lastSale = (await uow.Repository<SaleLine>().ListAsync(new Specification<SaleLine>().Where(l => l.Sale.Status == SaleStatus.Completed),
            l => new { l.ProductId, l.Sale.SoldAt }, cancellationToken)).GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Max(l => l.SoldAt));

        var byProduct = facts.GroupBy(f => f.ProductId).Select(g => new { Id = g.Key, g.First().Product, g.First().Category, g.First().Group,
            Boxes = g.Sum(f => f.Quantity), Revenue = g.Sum(f => f.Net), Profit = g.Sum(f => f.Net - f.Cost) }).ToList();
        var pareto = Pareto.Analyze(byProduct, x => x.Revenue);
        var rows = pareto.Items.Select(i => new ProductShare(i.Item.Product, Texts.Of(i.Item.Category), i.Item.Group, i.Item.Boxes, i.Item.Revenue, i.Item.Profit,
            i.Share, i.Cumulative, i.Class.ToString(), stock.GetValueOrDefault(i.Item.Id)?.Quantity ?? 0, stock.GetValueOrDefault(i.Item.Id)?.Value ?? 0m)).ToList();
        var sold = byProduct.Select(p => p.Id).ToHashSet();
        var slow = products.Where(p => !sold.Contains(p.Id) && (stock.GetValueOrDefault(p.Id)?.Quantity ?? 0) > 0)
            .Select(p => new SlowMover(p.Name, Texts.Of(p.Category), stock[p.Id].Quantity, stock[p.Id].Value, lastSale.TryGetValue(p.Id, out var ls) ? ls : null, stock[p.Id].NearestExpiry))
            .OrderByDescending(s => s.StockValue).ToList();
        var totalValue = stock.Values.Sum(s => s.Value);
        var byCat = products.GroupBy(p => p.Category).Select(g => new StockValueRow(Texts.Of(g.Key), g.Count(), g.Sum(p => stock.GetValueOrDefault(p.Id)?.Quantity ?? 0),
            g.Sum(p => stock.GetValueOrDefault(p.Id)?.Value ?? 0m), totalValue == 0 ? 0 : (double)(g.Sum(p => stock.GetValueOrDefault(p.Id)?.Value ?? 0m) / totalValue)))
            .OrderByDescending(r => r.Value).ToList();
        return new ProductReport(q.From, q.To, rows, slow, byCat, pareto.CountOf(AbcClass.A), pareto.ValueShareOf(AbcClass.A), pareto.CountShareOf(AbcClass.A),
            totalValue, facts.Sum(f => f.Cost));
    }
}

// ---------- 4. Depo ve cari raporu ----------

public sealed record SupplierRow(string Wholesaler, int Orders, int Boxes, int FreeBoxes, decimal Purchases, decimal FreeGoodsValue, double AverageLeadHours,
    decimal Paid, decimal Balance, decimal Overdue, double Share)
{
    public double FreeShare => Boxes == 0 ? 0 : FreeBoxes / (double)Boxes;
}

public sealed record SupplierReport(DateOnly From, DateOnly To, IReadOnlyList<SupplierRow> Wholesalers, string[] WeekLabels,
    IReadOnlyList<(string Name, double[] Values)> WeeklyPurchases, decimal TotalPurchases, decimal FreeGoods, decimal Balance, decimal Overdue, decimal Paid);

public sealed record GetSupplierReport(DateOnly From, DateOnly To) : IQuery<SupplierReport>;

public sealed class GetSupplierReportValidator : AbstractValidator<GetSupplierReport>
{
    public GetSupplierReportValidator() => RuleFor(x => new ReportPeriod(x.From, x.To)).SetValidator(new PeriodRules());
}

public sealed class GetSupplierReportHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetSupplierReport, SupplierReport>
{
    public async Task<Result<SupplierReport>> HandleAsync(GetSupplierReport q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var start = q.From.Start();
        var end = q.To.AddDays(1).Start();
        var wholesalers = await uow.Repository<Wholesaler>().ListAsync(new Specification<Wholesaler>().OrderBy(w => w.Name), cancellationToken);
        var orders = await uow.Repository<PurchaseOrder>().ListAsync(new Specification<PurchaseOrder>().Where(o => o.Status == OrderStatus.Received && o.ReceivedAt >= start && o.ReceivedAt < end),
            o => new { o.WholesalerId, o.ReceivedAt, o.OrderedAt, Lines = o.Lines.Select(l => new { l.Quantity, l.FreeQuantity, l.UnitPrice, l.Product.SalePrice }).ToList() },
            cancellationToken);
        var payments = await uow.Repository<SupplierPayment>().ListAsync(new Specification<SupplierPayment>().Where(p => p.Date >= q.From && p.Date <= q.To),
            p => new { p.WholesalerId, p.Amount }, cancellationToken);
        var ledger = await Ledger.LoadAsync(uow, null, cancellationToken);
        var total = orders.Sum(o => o.Lines.Sum(l => l.Quantity * l.UnitPrice));

        var rows = wholesalers.Select(w =>
        {
            var mine = orders.Where(o => o.WholesalerId == w.Id).ToList();
            var entries = ledger.Where(e => e.WholesalerId == w.Id).ToList();
            var purchases = mine.Sum(o => o.Lines.Sum(l => l.Quantity * l.UnitPrice));
            return new SupplierRow(w.Name, mine.Count, mine.Sum(o => o.Lines.Sum(l => l.Quantity + l.FreeQuantity)), mine.Sum(o => o.Lines.Sum(l => l.FreeQuantity)),
                purchases, mine.Sum(o => o.Lines.Sum(l => l.FreeQuantity * l.SalePrice)),
                mine.Where(o => o.OrderedAt is not null).Select(o => (o.ReceivedAt!.Value - o.OrderedAt!.Value).TotalHours).DefaultIfEmpty(0).Average(),
                payments.Where(p => p.WholesalerId == w.Id).Sum(p => p.Amount), entries.Sum(e => e.Debit - e.Credit), Ledger.Aging(entries, today).Overdue,
                total == 0 ? 0 : (double)(purchases / total));
        }).OrderByDescending(r => r.Purchases).ToList();

        var weeks = Math.Max(1, (int)Math.Ceiling((q.To.DayNumber - q.From.DayNumber + 1) / 7.0));
        var labels = Enumerable.Range(0, weeks).Select(i => q.From.AddDays(i * 7).ToString("dd.MM")).ToArray();
        var weekly = wholesalers.Select(w =>
        {
            var values = new double[weeks];
            foreach (var o in orders.Where(o => o.WholesalerId == w.Id))
            {
                var i = (DateOnly.FromDateTime(o.ReceivedAt!.Value).DayNumber - q.From.DayNumber) / 7;
                if (i >= 0 && i < weeks) values[i] += (double)o.Lines.Sum(l => l.Quantity * l.UnitPrice);
            }
            return (w.Name, values);
        }).ToList();
        return new SupplierReport(q.From, q.To, rows, labels, weekly, total, rows.Sum(r => r.FreeGoodsValue), rows.Sum(r => r.Balance), rows.Sum(r => r.Overdue),
            rows.Sum(r => r.Paid));
    }
}

// ---------- 5. Personel ve nöbet raporu ----------

public sealed record StaffLine(string Name, string Role, int Duties, int Points, int Sundays, int LeaveEntitlement, int LeaveUsed, int SickDays, int Sales,
    decimal Revenue, decimal AnnualCost)
{
    public int LeaveLeft => Math.Max(0, LeaveEntitlement - LeaveUsed);
}

public sealed record StaffReport(int Year, IReadOnlyList<StaffLine> Staff, string[] MonthLabels, IReadOnlyList<(string Name, double[] Values)> DutiesByMonth,
    int Duties, int Unstaffed, decimal PersonnelCost, double Fairness);

public sealed record GetStaffReport(int Year) : IQuery<StaffReport>;

public sealed class GetStaffReportHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetStaffReport, StaffReport>
{
    public async Task<Result<StaffReport>> HandleAsync(GetStaffReport q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var from = new DateOnly(q.Year, 1, 1);
        var to = new DateOnly(q.Year, 12, 31);
        var asOf = to < today ? to : today;
        await using var uow = uowFactory.Create();
        var employees = await uow.Repository<Employee>().ListAsync(new Specification<Employee>().Where(e => e.IsActive || e.LeftOn >= from).OrderBy(e => e.Role), cancellationToken);
        var duties = await uow.Repository<DutyShift>().ListAsync(new Specification<DutyShift>().Where(d => d.Date >= from && d.Date <= asOf), cancellationToken);
        var leaves = await uow.Repository<LeaveRecord>().ListAsync(new Specification<LeaveRecord>().Where(l => l.Start >= from && l.Start <= to), cancellationToken);
        var sales = await uow.Repository<Sale>().ListAsync(new Specification<Sale>().Where(s => s.SoldAt >= from.Start() && s.Status == SaleStatus.Completed && s.SoldById != null),
            s => new { s.SoldById, s.PatientShare, s.InstitutionShare }, cancellationToken);
        var months = Math.Max(1, asOf.Month);

        var staff = employees.Select(e =>
        {
            var mine = duties.Where(d => d.PharmacistId == e.Id || d.AssistantId == e.Id).ToList();
            var myLeaves = leaves.Where(l => l.EmployeeId == e.Id).ToList();
            var mySales = sales.Where(s => s.SoldById == e.Id).ToList();
            return new StaffLine(e.FullName, Texts.Of(e.Role), mine.Count, mine.Sum(d => d.Points), mine.Count(d => d.Kind != DutyKind.Night), e.AnnualLeaveEntitlement(asOf),
                myLeaves.Where(l => l.Type == LeaveType.Annual).Sum(l => l.Days), myLeaves.Where(l => l.Type == LeaveType.Sick).Sum(l => l.Days),
                mySales.Count, mySales.Sum(s => s.PatientShare + s.InstitutionShare), e.MonthlyGross * months);
        }).ToList();

        var labels = Enumerable.Range(1, 12).Select(m => Texts.Turkish.DateTimeFormat.GetAbbreviatedMonthName(m)).ToArray();
        var byMonth = employees.Where(e => e.Role != StaffRole.Intern).Select(e =>
        {
            var values = new double[12];
            foreach (var d in duties.Where(d => d.PharmacistId == e.Id || d.AssistantId == e.Id)) values[d.Date.Month - 1]++;
            return (e.FullName, values);
        }).ToList();

        // Adalet: aynı havuzdaki (eczacı / yardımcı) kişilerin puanlarının en düşüğünün en yükseğine oranı (1 = tam eşit)
        double Pool(bool pharmacist)
        {
            var pts = employees.Where(e => e.Role != StaffRole.Intern && e.IsPharmacist == pharmacist)
                .Select(e => duties.Where(d => d.PharmacistId == e.Id || d.AssistantId == e.Id).Sum(d => d.Points)).ToList();
            return pts.Count < 2 || pts.Max() == 0 ? 1 : pts.Min() / (double)pts.Max();
        }
        return new StaffReport(q.Year, staff, labels, byMonth, duties.Count, duties.Count(d => !d.IsStaffed), staff.Sum(s => s.AnnualCost), (Pool(true) + Pool(false)) / 2);
    }
}

// ---------- 6. Ciro tahmini ----------

public sealed record ForecastDay(DateOnly Date, double Forecast, double Lower, double Upper, double Prescriptions)
{
    public string DayName => Texts.DayName(Date);
}

public sealed record RevenueForecastReport(DateOnly Today, string Method, double Mae, double? Mape, IReadOnlyList<DateOnly> HistoryDates, double[] HistoryRevenue,
    double[] Fitted, IReadOnlyList<ForecastDay> Days, double HistoryAverage)
{
    public double ForecastTotal => Days.Sum(d => d.Forecast);
    public double ForecastAverage => Days.Count == 0 ? 0 : ForecastTotal / Days.Count;
}

public sealed record GetRevenueForecast(int Horizon = 14, int HistoryDays = 56) : IQuery<RevenueForecastReport>;

public sealed class GetRevenueForecastValidator : AbstractValidator<GetRevenueForecast>
{
    public GetRevenueForecastValidator()
    {
        RuleFor(x => x.Horizon).InclusiveBetween(1, 60).WithName("Tahmin süresi");
        RuleFor(x => x.HistoryDays).InclusiveBetween(21, 365).WithName("Geçmiş gün sayısı");
    }
}

public sealed class GetRevenueForecastHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetRevenueForecast, RevenueForecastReport>
{
    public async Task<Result<RevenueForecastReport>> HandleAsync(GetRevenueForecast q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var histFrom = today.AddDays(-q.HistoryDays);
        await using var uow = uowFactory.Create();
        var sales = await uow.Repository<Sale>().ListAsync(new Specification<Sale>().Where(s => s.SoldAt >= histFrom.Start() && s.SoldAt < today.Start() && s.Status == SaleStatus.Completed),
            s => new { s.SoldAt, s.Kind, s.PatientShare, s.InstitutionShare }, cancellationToken);
        var dates = Enumerable.Range(0, q.HistoryDays).Select(i => histFrom.AddDays(i)).ToList();
        var byDay = sales.GroupBy(s => DateOnly.FromDateTime(s.SoldAt)).ToDictionary(g => g.Key, g => g.ToList());
        var revenue = dates.Select(d => (double)(byDay.GetValueOrDefault(d)?.Sum(s => s.PatientShare + s.InstitutionShare) ?? 0m)).ToArray();
        var rxCounts = dates.Select(d => (double)(byDay.GetValueOrDefault(d)?.Count(s => s.Kind == SaleKind.Prescription) ?? 0)).ToArray();
        // Eczane talebi güçlü haftalık desen taşır (pazar kapalı, pazartesi yoğun); yeterli geçmiş varsa doğrudan Holt-Winters kullanılır.
        ForecastResult Fit(double[] y) => y.Length >= 7 * 3 ? Forecast.HoltWinters(y, 7, q.Horizon) : Forecast.Auto(y, q.Horizon, seasonLength: 7);
        var model = Fit(revenue).Clamp(0, double.MaxValue);
        var rxModel = Fit(rxCounts).Clamp(0, double.MaxValue);
        var days = Enumerable.Range(0, q.Horizon).Select(i => new ForecastDay(today.AddDays(i), Math.Round(model.Forecast[i], 0), Math.Round(model.Lower[i], 0),
            Math.Round(model.Upper[i], 0), Math.Round(rxModel.Forecast[i], 0))).ToList();
        return new RevenueForecastReport(today, model.MethodName, model.Mae, model.Mape, dates, revenue, model.Fitted, days, revenue.Length == 0 ? 0 : revenue.Average());
    }
}
