using Eczane.Application;
using Eczane.Domain;
using Eczane.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application.Messaging;
using OnderCode.Data.Initialization;

namespace Eczane.Tests;

/// <summary>Örnek veriyle dolu gerçek veritabanı üzerinde satış, stok, depo, nöbet ve rapor akışları.</summary>
public sealed class EczaneFlowTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ondercode-tests", Guid.NewGuid().ToString("N"));
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));
    private ServiceProvider _services = null!;

    private ISender Sender => _services.GetRequiredService<ISender>();
    private DateOnly Today => _time.Today();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddEczane(o => o.DataDirectory = _dir);
        _services = services.BuildServiceProvider();
        await _services.GetRequiredService<DatabaseInitializer<EczaneDbContext>>().InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<T> Ok<T>(IRequest<OnderCode.Application.Results.Result<T>> request)
    {
        var r = await Sender.SendAsync(request);
        Assert.True(r.IsSuccess, string.Join(" | ", r.Errors.Select(e => e.Message)));
        return r.Value;
    }

    [Fact]
    public async Task DemoData_Is_Lively()
    {
        var d = await Ok(new GetDashboard());
        Assert.True(d.Products > 150);
        Assert.True(d.StockValue > 100_000m);
        Assert.True(d.RxTrend.Sum() > 500_000);
        Assert.True(d.RetailTrend.Sum() > 100_000);
        Assert.Equal(5, d.TopHospitals.Count);
        Assert.NotNull(d.NextDuty);
        Assert.Contains(d.OnLeaveToday, s => s.Contains("Büşra"));
        Assert.True(d.InstitutionReceivable > 0);

        var products = await Ok(new ListProducts());
        Assert.Contains(products, p => p.Stock > 0 && p.Sold30 > 20);
        Assert.Contains(products, p => p.ColdChain);
        Assert.DoesNotContain(products, p => p.Stock < 0);

        var expiry = await Ok(new ListExpiry());
        Assert.Contains(expiry, e => e.Level == ExpiryLevel.Critical || e.Level == ExpiryLevel.Expired);

        var hospitals = await Ok(new ListHospitals());
        Assert.Equal(10, hospitals.Count);
        Assert.InRange(hospitals.Sum(h => h.Share), 0.99, 1.01);

        var patients = await Ok(new ListPatients());
        Assert.Equal(1100, patients.Count);
        var regular = patients.OrderByDescending(p => p.Prescriptions).First();
        var history = await Ok(new GetPatientHistory(regular.Id));
        Assert.Contains(history.Medications, m => m.Times >= 2);
    }

    [Fact]
    public async Task Prescription_Sale_Uses_Fefo_And_Return_Restores_Stock()
    {
        var catalog = await Ok(new GetSaleCatalog());
        var products = await Ok(new ListProducts(Category: ProductCategory.Rx));
        var item = catalog.First(c => c.IsRx && c.Color == PrescriptionColor.White && c.Stock >= 3 && c.Reimbursed);
        var card = await Ok(new GetProductCard(item.Id));
        var nearest = card.Lots.Where(l => l.Level != ExpiryLevel.Expired).OrderBy(l => l.Expiry).First();
        var patient = (await Ok(new GetPatientOptions())).First(p => p.Insurance == InsuranceType.Sgk && !p.ChronicReport);
        var hospital = (await Ok(new GetHospitalOptions())).First();
        var draft = new SaleDraft(SaleKind.Prescription, [new SaleItem(item.Id, 1)], patient.Id, hospital.Id, "Kardiyoloji", PrescriptionColor.White,
            "TST0001", Today, PaymentType.Card);

        var quote = await Ok(new QuoteSale(draft));
        Assert.Equal(item.Price, quote.Total);
        Assert.Equal(quote.Total, quote.PatientShare + quote.InstitutionShare);
        Assert.True(quote.InstitutionShare > 0);
        Assert.Equal(card.Stock, (await Ok(new GetProductCard(item.Id))).Stock); // önizleme stoğa dokunmaz

        var receipt = await Ok(new CreateSale(draft));
        Assert.Equal(nearest.LotNo, Assert.Single(receipt.Lines).LotNo);
        Assert.Equal(card.Stock - 1, (await Ok(new GetProductCard(item.Id))).Stock);
        var listed = await Ok(new ListSales { Period = SalePeriod.Today, Search = "TST0001" });
        Assert.Equal(receipt.SaleId, Assert.Single(listed.Items).Id);

        Assert.True((await Sender.SendAsync(new ReturnSale(receipt.SaleId))).IsSuccess);
        Assert.Equal(card.Stock, (await Ok(new GetProductCard(item.Id))).Stock);
        Assert.False((await Sender.SendAsync(new ReturnSale(receipt.SaleId))).IsSuccess);
        _ = products;
    }

    [Fact]
    public async Task Rx_Product_Cannot_Be_Sold_Without_Prescription_And_Stock_Is_Checked()
    {
        var catalog = await Ok(new GetSaleCatalog());
        var rx = catalog.First(c => c.IsRx && c.Stock > 0);
        var r = await Sender.SendAsync(new CreateSale(new SaleDraft(SaleKind.Retail, [new SaleItem(rx.Id, 1)])));
        Assert.False(r.IsSuccess);
        var otc = catalog.First(c => !c.IsRx && c.Stock > 0);
        var tooMany = await Sender.SendAsync(new CreateSale(new SaleDraft(SaleKind.Retail, [new SaleItem(otc.Id, Math.Min(100, otc.Stock + 1))])));
        Assert.Equal(otc.Stock + 1 <= 100, !tooMany.IsSuccess);
        var ok = await Ok(new CreateSale(new SaleDraft(SaleKind.Retail, [new SaleItem(otc.Id, 1)], Payment: PaymentType.Cash)));
        Assert.Equal(otc.Price, ok.PatientShare);
        Assert.Equal(0m, ok.InstitutionShare);
    }

    [Fact]
    public async Task Order_Receive_And_Pay_Updates_Stock_And_Ledger()
    {
        var suggestions = await Ok(new GetReorderSuggestions());
        Assert.NotEmpty(suggestions);
        var s = suggestions[0];
        var before = (await Ok(new GetProductCard(s.ProductId))).Stock;
        var wholesalerBefore = (await Ok(new ListWholesalers())).First(w => w.Id == s.WholesalerId);

        var orderId = await Ok(new CreatePurchaseOrder(s.WholesalerId, [new OrderItem(s.ProductId, s.Suggested)]));
        var order = await Ok(new GetOrder(orderId));
        Assert.Equal(OrderStatus.Ordered, order.Order.Status);
        var line = Assert.Single(order.Lines);
        Assert.Equal(s.Free, line.Free);
        Assert.DoesNotContain(await Ok(new GetReorderSuggestions()), x => x.ProductId == s.ProductId);

        var boxes = await Ok(new ReceiveOrder(orderId, [new ReceivedLot(line.LineId, line.LotNo, line.Expiry)]));
        Assert.Equal(line.Quantity + line.Free, boxes);
        Assert.Equal(before + boxes, (await Ok(new GetProductCard(s.ProductId))).Stock);
        var wholesalerAfter = (await Ok(new ListWholesalers())).First(w => w.Id == s.WholesalerId);
        Assert.Equal(wholesalerBefore.Balance + line.LineTotal, wholesalerAfter.Balance);

        await Ok(new AddSupplierPayment(s.WholesalerId, Today, line.LineTotal, SupplierPaymentMethod.Transfer, "Test"));
        Assert.Equal(wholesalerBefore.Balance, (await Ok(new ListWholesalers())).First(w => w.Id == s.WholesalerId).Balance);
        var statement = await Ok(new GetWholesalerStatement(s.WholesalerId));
        Assert.Equal(wholesalerBefore.Balance, statement.Balance);
        Assert.False((await Sender.SendAsync(new ReceiveOrder(orderId, []))).IsSuccess);
    }

    [Fact]
    public async Task Dispose_And_Count_Write_Movements()
    {
        var lot = (await Ok(new ListExpiry())).First(e => e.Quantity >= 2);
        Assert.True((await Sender.SendAsync(new DisposeLot(lot.LotId, 1, "Kırık"))).IsSuccess);
        var diff = await Ok(new CountLot(lot.LotId, lot.Quantity + 2));
        Assert.Equal(3, diff);
        var card = await Ok(new GetProductCard(lot.ProductId));
        Assert.Contains(card.Movements, m => m.Type == "İmha" && m.Out == 1);
        Assert.Contains(card.Movements, m => m.Type == "Sayım farkı" && m.In == 3);
    }

    [Fact]
    public async Task Duty_Planning_Fills_Next_Month_Fairly()
    {
        var next = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1);
        var month = await Ok(new GetDutyMonth(next.Year, next.Month));
        Assert.True(month.Duties >= 3);
        Assert.Equal(month.Duties, month.Unstaffed);
        var assigned = await Ok(new PlanDutyMonth(next.Year, next.Month));
        Assert.Equal(month.Duties, assigned);
        var after = await Ok(new GetDutyMonth(next.Year, next.Month));
        Assert.Equal(0, after.Unstaffed);
        var employees = await Ok(new GetEmployeeOptions());
        Assert.All(after.Days.Where(d => d.IsDuty), d => Assert.True(employees.First(e => e.Id == d.PharmacistId).IsPharmacist));

        // Bu ay: geçmiş günler dolu
        var current = await Ok(new GetDutyMonth(Today.Year, Today.Month));
        Assert.DoesNotContain(current.Days, d => d.Date < Today && d.Unstaffed);
    }

    [Fact]
    public async Task Leave_Rules()
    {
        var employees = await Ok(new ListEmployees());
        var mert = employees.First(e => e.FullName.StartsWith("Mert"));
        var start = Today.AddDays(30);
        await Ok(new AddLeave(mert.Id, LeaveType.Excuse, start, start, "Test"));
        Assert.False((await Sender.SendAsync(new AddLeave(mert.Id, LeaveType.Sick, start, start.AddDays(1), null))).IsSuccess);
        Assert.False((await Sender.SendAsync(new AddLeave(mert.Id, LeaveType.Annual, Today.AddDays(40), Today.AddDays(70), null))).IsSuccess);
        var intern = employees.First(e => e.Role == StaffRole.Intern);
        Assert.Equal(0, intern.LeaveEntitlement);
    }

    [Fact]
    public async Task Reports_Are_Consistent()
    {
        var from = Today.AddDays(-28);
        var to = Today.AddDays(-1);
        var sales = await Ok(new GetSalesReport(from, to));
        Assert.Equal(28, sales.Days.Count);
        Assert.True(sales.Total > 0 && sales.Profit > 0 && sales.Profit < sales.Total);
        Assert.InRange(sales.Margin, 0.05, 0.5);
        Assert.Equal(sales.Total, sales.Categories.Sum(c => c.Revenue), 0);
        Assert.True(sales.Days.Where(d => d.Date.DayOfWeek == DayOfWeek.Sunday).All(d => d.Count < sales.Days.Where(x => x.Date.DayOfWeek == DayOfWeek.Monday).Min(x => x.Count)));
        var list = await Ok(new ListSales { Period = SalePeriod.All, PageSize = 1000, Kind = SaleKind.Prescription, Search = null });
        Assert.True(list.TotalCount > 1000);

        var hosp = await Ok(new GetHospitalReport(from, to));
        Assert.Equal(sales.Prescriptions, hosp.Prescriptions);
        Assert.InRange(hosp.Hospitals.Sum(h => h.Share), 0.99, 1.01);
        Assert.Equal("A", hosp.Hospitals[0].Class);
        Assert.Contains(hosp.Branches, b => b.Name == "Aile hekimliği");
        Assert.True(hosp.TopTrend.Count == 5);

        var prod = await Ok(new GetProductReport(from, to));
        Assert.True(prod.ACount > 0 && prod.ACount < prod.Products.Count / 2);
        Assert.True(prod.Turnover > 0);

        var sup = await Ok(new GetSupplierReport(from, to));
        Assert.Equal(4, sup.Wholesalers.Count);
        Assert.True(sup.TotalPurchases > 0 && sup.FreeGoods > 0);
        Assert.True(sup.Overdue > 0);

        var staff = await Ok(new GetStaffReport(Today.Year));
        Assert.True(staff.Duties > 0);
        Assert.InRange(staff.Fairness, 0.5, 1.0);

        var forecast = await Ok(new GetRevenueForecast(14));
        Assert.Equal(14, forecast.Days.Count);
        Assert.True(forecast.ForecastTotal > 0);
    }

    [Fact]
    public async Task Invoices_And_Assets()
    {
        var invoices = await Ok(new ListInvoices());
        Assert.Contains(invoices, i => i.Id is not null && i.PaidOn is null);
        var current = invoices.First(i => i.Id is null && i.Year == Today.Year && i.Month == Today.Month);
        Assert.False((await Sender.SendAsync(new IssueInvoice(current.Institution, current.Year, current.Month))).IsSuccess);
        var open = invoices.First(i => i.Id is int && i.PaidOn is null);
        Assert.True((await Sender.SendAsync(new MarkInvoicePaid(open.Id!.Value, Today))).IsSuccess);

        var assets = await Ok(new ListAssets());
        Assert.True(assets.Count >= 20);
        Assert.All(assets, a => Assert.InRange(a.BookValue, 0, a.Cost));
        Assert.Contains(assets, a => a.Status == AssetStatus.InRepair);
        Assert.Contains(assets, a => a.NextMaintenance is not null && a.MaintenanceDays <= 30);
        var withRetired = await Ok(new ListAssets(IncludeRetired: true));
        Assert.Equal(assets.Count + 1, withRetired.Count);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
