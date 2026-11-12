using Eczane.Domain;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace Eczane.Application;

public sealed record DashboardSale(DateTime At, string Number, string KindText, string? Patient, string? Hospital, int Boxes, decimal Amount, string PaymentText);

public sealed record TopHospital(string Name, int Prescriptions, double Share);

public sealed record DashboardView(
    DateTime Now,
    decimal TodayRevenue, decimal TodayProfit, decimal YesterdayToNow, int TodayPrescriptions, int TodayRetail, decimal TodayCash, decimal TodayCard,
    int OutOfStock, int LowStock, int ExpiringLots, decimal ExpiringValue, decimal StockValue, int Products,
    decimal PayableOverdue, decimal PayableNext7, decimal InstitutionReceivable,
    DateOnly? NextDuty, string? NextDutyPharmacist, string? NextDutyKind, int UnstaffedDuties, IReadOnlyList<string> OnLeaveToday, int MaintenanceDue,
    string[] TrendLabels, double[] RxTrend, double[] RetailTrend, string[] HourLabels, double[] SalesByHourToday, double[] SalesByHourTypical,
    IReadOnlyList<TopHospital> TopHospitals, IReadOnlyList<DashboardSale> RecentSales, IReadOnlyList<string> Alerts)
{
    public int TodayCount => TodayPrescriptions + TodayRetail;
    public decimal AverageBasket => TodayCount == 0 ? 0 : TodayRevenue / TodayCount;
}

public sealed record GetDashboard : IQuery<DashboardView>;

public sealed class GetDashboardHandler(IUnitOfWorkFactory uowFactory, TimeProvider time, ISender sender) : IQueryHandler<GetDashboard, DashboardView>
{
    public async Task<Result<DashboardView>> HandleAsync(GetDashboard q, CancellationToken cancellationToken)
    {
        var now = time.Now();
        var today = DateOnly.FromDateTime(now);
        var from = today.AddDays(-29);
        var typicalFrom = today.AddDays(-28);
        await using var uow = uowFactory.Create();

        var sales = await uow.Repository<Sale>().ListAsync(new Specification<Sale>().Where(s => s.SoldAt >= typicalFrom.Start() && s.Status == SaleStatus.Completed),
            s => new { s.SoldAt, s.Kind, s.PatientShare, s.InstitutionShare, s.Cost, s.Payment, s.Number, Patient = s.Patient!.FullName, Hospital = s.Hospital!.Name,
                Boxes = s.Lines.Sum(l => l.Quantity) }, cancellationToken);
        var todays = sales.Where(s => s.SoldAt >= today.Start()).ToList();
        var yesterdayNow = now.AddDays(-1);
        var yesterday = sales.Where(s => s.SoldAt >= today.AddDays(-1).Start() && s.SoldAt <= yesterdayNow).ToList();

        var labels = new string[30];
        var rx = new double[30];
        var retail = new double[30];
        for (var i = 0; i < 30; i++) labels[i] = from.AddDays(i).ToString("dd.MM");
        foreach (var s in sales)
        {
            var idx = DateOnly.FromDateTime(s.SoldAt).DayNumber - from.DayNumber;
            if (idx is < 0 or >= 30) continue;
            if (s.Kind == SaleKind.Prescription) rx[idx] += (double)(s.PatientShare + s.InstitutionShare);
            else retail[idx] += (double)s.PatientShare;
        }

        const int firstHour = 8, hours = 15; // 08-22
        var hourLabels = Enumerable.Range(firstHour, hours).Select(h => $"{h:00}").ToArray();
        var byHourToday = new double[hours];
        var byHourTypical = new double[hours];
        foreach (var s in todays) { var h = s.SoldAt.Hour - firstHour; if (h is >= 0 and < hours) byHourToday[h]++; }
        var sameDays = sales.Where(s => s.SoldAt < today.Start() && s.SoldAt.DayOfWeek == now.DayOfWeek).ToList();
        var weeks = Math.Max(1, sameDays.Select(s => s.SoldAt.Date).Distinct().Count());
        foreach (var s in sameDays) { var h = s.SoldAt.Hour - firstHour; if (h is >= 0 and < hours) byHourTypical[h] += 1.0 / weeks; }

        var thirty = sales.Where(s => s.SoldAt >= from.Start() && s.Kind == SaleKind.Prescription && s.Hospital is not null).ToList();
        var top = thirty.GroupBy(s => s.Hospital!).Select(g => new TopHospital(g.Key, g.Count(), thirty.Count == 0 ? 0 : g.Count() / (double)thirty.Count))
            .OrderByDescending(t => t.Prescriptions).Take(5).ToList();

        // Stok
        var stock = await StockQueries.OnHandAsync(uow, today, cancellationToken);
        var products = await uow.Repository<Product>().ListAsync(new Specification<Product>().Where(p => p.IsActive), p => new { p.Id, p.MinStock }, cancellationToken);
        var outOf = products.Count(p => (stock.GetValueOrDefault(p.Id)?.Quantity ?? 0) == 0);
        var low = products.Count(p => stock.GetValueOrDefault(p.Id)?.Quantity is int qty && qty > 0 && qty <= p.MinStock);
        var critical = today.AddDays(StockRules.CriticalDays);
        var expiring = await uow.Repository<StockLot>().ListAsync(new Specification<StockLot>().Where(l => l.Quantity > 0 && l.Expiry <= critical),
            l => new { l.Quantity, l.UnitCost }, cancellationToken);

        // Cari ve kurum alacağı
        var ledger = await Ledger.LoadAsync(uow, null, cancellationToken);
        decimal overdue = 0, next7 = 0;
        foreach (var g in ledger.GroupBy(e => e.WholesalerId))
        {
            var (o, nextDue, amount) = Ledger.Aging(g.ToList(), today);
            overdue += o;
            if (nextDue is { } d && d <= today.AddDays(7)) next7 += amount;
        }
        var invoices = await sender.SendAsync(new ListInvoices(), cancellationToken);
        var receivable = invoices.IsSuccess ? invoices.Value.Where(i => i.PaidOn is null).Sum(i => i.Amount) : 0m;

        // Nöbet, izin, bakım
        var duties = await uow.Repository<DutyShift>().ListAsync(new Specification<DutyShift>().Where(d => d.Date >= today && d.Date <= today.AddDays(45))
            .Include(d => d.Pharmacist).OrderBy(d => d.Date), cancellationToken);
        var nextDuty = duties.FirstOrDefault();
        var leaves = await uow.Repository<LeaveRecord>().ListAsync(new Specification<LeaveRecord>().Where(l => l.Start <= today && l.End >= today).Include(l => l.Employee), cancellationToken);
        var maintLimit = today.AddDays(30);
        var maintenance = await uow.Repository<Asset>().CountAsync(new Specification<Asset>().Where(a => a.Status != AssetStatus.Retired && a.NextMaintenance != null && a.NextMaintenance <= maintLimit), cancellationToken);

        var alerts = new List<string>();
        if (outOf > 0) alerts.Add($"{outOf} ürün stokta yok");
        if (expiring.Count > 0) alerts.Add($"{expiring.Count} partinin SKT'sine {StockRules.CriticalDays} günden az kaldı ({expiring.Sum(l => l.Quantity * l.UnitCost):N0} ₺)");
        if (overdue > 0) alerts.Add($"Depolara vadesi geçmiş {overdue:N0} ₺ borç");
        var unstaffed = duties.Count(d => !d.IsStaffed);
        if (unstaffed > 0) alerts.Add($"{unstaffed} nöbetin görevlisi yok");
        if (maintenance > 0) alerts.Add($"{maintenance} demirbaşın bakımı 30 gün içinde");

        var recent = todays.OrderByDescending(s => s.SoldAt).Take(40).Select(s => new DashboardSale(s.SoldAt, s.Number, Texts.Of(s.Kind), s.Patient, s.Hospital, s.Boxes,
            s.PatientShare + s.InstitutionShare, s.Payment is { } p ? Texts.Of(p) : "")).ToList();

        return new DashboardView(now,
            todays.Sum(s => s.PatientShare + s.InstitutionShare), todays.Sum(s => s.PatientShare + s.InstitutionShare - s.Cost),
            yesterday.Sum(s => s.PatientShare + s.InstitutionShare), todays.Count(s => s.Kind == SaleKind.Prescription), todays.Count(s => s.Kind == SaleKind.Retail),
            todays.Where(s => s.Payment == PaymentType.Cash).Sum(s => s.PatientShare), todays.Where(s => s.Payment == PaymentType.Card).Sum(s => s.PatientShare),
            outOf, low, expiring.Count, expiring.Sum(l => l.Quantity * l.UnitCost), stock.Values.Sum(s => s.Value), products.Count,
            overdue, next7, receivable,
            nextDuty?.Date, nextDuty?.Pharmacist?.FullName, nextDuty is null ? null : Texts.Of(nextDuty.Kind), unstaffed,
            leaves.Select(l => $"{l.Employee.FullName} ({Texts.Of(l.Type).ToLower(Texts.Turkish)})").ToList(), maintenance,
            labels, rx, retail, hourLabels, byHourToday, byHourTypical, top, recent, alerts);
    }
}
