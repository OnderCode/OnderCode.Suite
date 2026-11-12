using Eczane.Domain;
using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace Eczane.Application;

// ---------- Depolar ve cari ----------

public sealed record WholesalerRow(int Id, string Name, int DeliveriesPerDay, int PaymentTermDays, decimal DiscountRate,
    decimal Purchases90, int Orders90, decimal FreeGoods90, decimal Balance, decimal Overdue, DateOnly? NextDue, decimal NextDueAmount,
    DateTime? LastDelivery, int Campaigns, double OnTimeRate)
{
    public string DiscountText => $"%{DiscountRate * 100:0.#}";
    public string TermText => $"{PaymentTermDays} gün";
}

public sealed record ListWholesalers : IQuery<IReadOnlyList<WholesalerRow>>;

public sealed class ListWholesalersHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListWholesalers, IReadOnlyList<WholesalerRow>>
{
    public async Task<Result<IReadOnlyList<WholesalerRow>>> HandleAsync(ListWholesalers q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var wholesalers = await uow.Repository<Wholesaler>().ListAsync(new Specification<Wholesaler>().OrderBy(w => w.Name), cancellationToken);
        var ledger = await Ledger.LoadAsync(uow, null, cancellationToken);
        var from = today.AddDays(-89).Start();
        var campaigns = await uow.Repository<FreeGoodsCampaign>().ListAsync(new Specification<FreeGoodsCampaign>().Where(c => c.ValidUntil >= today),
            c => c.WholesalerId, cancellationToken);
        return wholesalers.Select(w =>
        {
            var mine = ledger.Where(e => e.WholesalerId == w.Id).ToList();
            var invoices = mine.Where(e => e.Debit > 0).ToList();
            var recent = invoices.Where(e => e.At >= from).ToList();
            var (overdue, nextDue, nextAmount) = Ledger.Aging(mine, today);
            var onTime = recent.Count == 0 ? 1 : recent.Count(e => e.LeadHours <= 24.0 / w.DeliveriesPerDay + 4) / (double)recent.Count;
            return new WholesalerRow(w.Id, w.Name, w.DeliveriesPerDay, w.PaymentTermDays, w.DiscountRate,
                recent.Sum(e => e.Debit), recent.Count, recent.Sum(e => e.FreeValue), mine.Sum(e => e.Debit - e.Credit), overdue, nextDue, nextAmount,
                invoices.Count == 0 ? null : invoices.Max(e => e.At), campaigns.Count(c => c == w.Id), onTime);
        }).ToList();
    }
}

public sealed record LedgerEntry(int WholesalerId, DateTime At, string Type, string Reference, decimal Debit, decimal Credit, DateOnly? DueDate,
    decimal FreeValue, double LeadHours)
{
    public decimal Balance { get; init; }
}

internal static class Ledger
{
    /// <summary>Cari hareketler: teslim alınan sipariş borç, ödeme alacak.</summary>
    public static async Task<List<LedgerEntry>> LoadAsync(IUnitOfWork uow, int? wholesalerId, CancellationToken ct)
    {
        var orderSpec = new Specification<PurchaseOrder>().Where(o => o.Status == OrderStatus.Received);
        var paySpec = new Specification<SupplierPayment>();
        if (wholesalerId is int id)
        {
            orderSpec.Where(o => o.WholesalerId == id);
            paySpec.Where(p => p.WholesalerId == id);
        }
        // Ondalık çarpımlar bellekte yapılır (tutarlar veritabanında ölçekli tamsayıdır)
        var orders = (await uow.Repository<PurchaseOrder>().ListAsync(orderSpec, o => new
        {
            o.WholesalerId, o.ReceivedAt, o.OrderedAt, o.Number, o.DueDate,
            Lines = o.Lines.Select(l => new { l.Quantity, l.FreeQuantity, l.UnitPrice, l.Product.SalePrice }).ToList(),
        }, ct)).Select(o => new
        {
            o.WholesalerId, o.ReceivedAt, o.OrderedAt, o.Number, o.DueDate,
            Total = o.Lines.Sum(l => l.Quantity * l.UnitPrice),
            Free = o.Lines.Sum(l => l.FreeQuantity * l.SalePrice),
        }).ToList();
        var payments = await uow.Repository<SupplierPayment>().ListAsync(paySpec, p => new { p.WholesalerId, p.Date, p.Amount, p.Method, p.Note }, ct);
        var list = orders.Select(o => new LedgerEntry(o.WholesalerId, o.ReceivedAt!.Value, "Fatura", o.Number, o.Total, 0m, o.DueDate, o.Free,
                o.OrderedAt is { } placed ? (o.ReceivedAt!.Value - placed).TotalHours : 0))
            .Concat(payments.Select(p => new LedgerEntry(p.WholesalerId, p.Date.ToDateTime(new TimeOnly(15, 0)), "Ödeme · " + Texts.Of(p.Method), p.Note ?? "", 0m, p.Amount, null, 0m, 0)))
            .OrderBy(e => e.At).ToList();
        return list;
    }

    /// <summary>
    /// Vade analizi: ödemeler en eski faturadan başlayarak kapatılır (FIFO). Kalan faturalardan vadesi geçenler toplamı,
    /// bir sonraki vade günü ve o gün ödenecek tutar döner.
    /// </summary>
    public static (decimal Overdue, DateOnly? NextDue, decimal NextAmount) Aging(IReadOnlyList<LedgerEntry> entries, DateOnly today)
    {
        var paid = entries.Sum(e => e.Credit);
        var open = new List<(DateOnly Due, decimal Amount)>();
        foreach (var inv in entries.Where(e => e.Debit > 0).OrderBy(e => e.DueDate))
        {
            var cover = Math.Min(paid, inv.Debit);
            paid -= cover;
            if (inv.Debit - cover > 0) open.Add((inv.DueDate ?? DateOnly.FromDateTime(inv.At), inv.Debit - cover));
        }
        var overdue = open.Where(o => o.Due < today).Sum(o => o.Amount);
        var next = open.Where(o => o.Due >= today).OrderBy(o => o.Due).FirstOrDefault();
        return (overdue, next == default ? null : next.Due, next == default ? 0m : open.Where(o => o.Due == next.Due).Sum(o => o.Amount));
    }
}

public sealed record WholesalerStatement(int Id, string Name, IReadOnlyList<LedgerEntry> Entries, decimal Balance, decimal Overdue);

public sealed record GetWholesalerStatement(int WholesalerId) : IQuery<WholesalerStatement>;

public sealed class GetWholesalerStatementHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetWholesalerStatement, WholesalerStatement>
{
    public async Task<Result<WholesalerStatement>> HandleAsync(GetWholesalerStatement q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var w = await uow.Repository<Wholesaler>().GetRequiredAsync(q.WholesalerId, cancellationToken);
        var entries = await Ledger.LoadAsync(uow, w.Id, cancellationToken);
        var running = 0m;
        var withBalance = entries.Select(e => e with { Balance = running += e.Debit - e.Credit }).ToList();
        withBalance.Reverse();
        return new WholesalerStatement(w.Id, w.Name, withBalance, running, Ledger.Aging(entries, time.Today()).Overdue);
    }
}

public sealed record AddSupplierPayment(int WholesalerId, DateOnly Date, decimal Amount, SupplierPaymentMethod Method, string? Note) : ICommand<int>;

public sealed class AddSupplierPaymentValidator : AbstractValidator<AddSupplierPayment>
{
    public AddSupplierPaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).LessThan(100_000_000).WithName("Tutar");
        RuleFor(x => x.Note).MaximumLength(150).WithName("Açıklama");
    }
}

public sealed class AddSupplierPaymentHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<AddSupplierPayment, int>
{
    public async Task<Result<int>> HandleAsync(AddSupplierPayment c, CancellationToken cancellationToken)
    {
        if (c.Date > time.Today()) throw new DomainException("İleri tarihli ödeme girilemez.");
        await using var uow = uowFactory.Create();
        var w = await uow.Repository<Wholesaler>().GetRequiredAsync(c.WholesalerId, cancellationToken);
        var payment = new SupplierPayment(w, c.Date, c.Amount, c.Method, c.Note);
        uow.Repository<SupplierPayment>().Add(payment);
        await uow.SaveChangesAsync(cancellationToken);
        return payment.Id;
    }
}

// ---------- Sipariş önerisi ----------

public sealed record SuggestionRow(int ProductId, string Product, string Barcode, ProductCategory Category, int Stock, int OnOrder, int MinStock,
    double DailyUsage, int Suggested, int WholesalerId, string Wholesaler, decimal UnitPrice, int Free, string? Campaign)
{
    public decimal LineTotal => Suggested * UnitPrice;
    public int? DaysOfCover => DailyUsage <= 0 ? null : (int)Math.Floor(Stock / DailyUsage);
    public string CategoryText => Texts.Of(Category);
    public decimal EffectiveUnitCost => Suggested + Free == 0 ? UnitPrice : LineTotal / (Suggested + Free);
}

/// <summary>
/// Sipariş önerisi: son 28 günün satış hızı, en az stok, eldeki ve yoldaki miktar. Her ürün için toplam maliyeti en düşük depo
/// seçilir (depo iskontosu + varsa mal fazlası kampanyası; kampanya eşiğine yuvarlamak %30'dan fazla kutu eklemiyorsa yuvarlanır).
/// </summary>
public sealed record GetReorderSuggestions(double SafetyDays = 7) : IQuery<IReadOnlyList<SuggestionRow>>;

public sealed class GetReorderSuggestionsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetReorderSuggestions, IReadOnlyList<SuggestionRow>>
{
    public async Task<Result<IReadOnlyList<SuggestionRow>>> HandleAsync(GetReorderSuggestions q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var products = await uow.Repository<Product>().ListAsync(new Specification<Product>().Where(p => p.IsActive),
            p => new { p.Id, p.Name, p.Barcode, p.Category, p.MinStock, p.SalePrice, p.PurchasePrice }, cancellationToken);
        var stock = await StockQueries.OnHandAsync(uow, today, cancellationToken);
        var onOrder = await StockQueries.OnOrderAsync(uow, cancellationToken);
        var sold = await StockQueries.SoldAsync(uow, today.AddDays(-28).Start(), today.AddDays(1).Start(), cancellationToken);
        var wholesalers = await uow.Repository<Wholesaler>().ListAsync(new Specification<Wholesaler>().Where(w => w.IsActive), cancellationToken);
        var campaigns = await uow.Repository<FreeGoodsCampaign>().ListAsync(new Specification<FreeGoodsCampaign>().Where(c => c.ValidUntil >= today), cancellationToken);

        var rows = new List<SuggestionRow>();
        foreach (var p in products)
        {
            var usage = sold.GetValueOrDefault(p.Id) / 28.0;
            var have = stock.GetValueOrDefault(p.Id)?.Quantity ?? 0;
            var coming = onOrder.GetValueOrDefault(p.Id);
            var need = StockRules.SuggestOrder(usage, have, coming, p.MinStock, 1, q.SafetyDays);
            if (need == 0) continue;
            var best = Pricing.Best(wholesalers, campaigns, p.Id, p.SalePrice, p.Category, need);
            if (best is null) continue;
            rows.Add(new SuggestionRow(p.Id, p.Name, p.Barcode, p.Category, have, coming, p.MinStock, usage, best.Quantity, best.Wholesaler.Id,
                best.Wholesaler.Name, best.UnitPrice, best.Free, best.Campaign?.Text));
        }
        return rows.OrderByDescending(r => r.Stock == 0).ThenBy(r => r.DaysOfCover ?? int.MaxValue).ThenBy(r => r.Product).ToList();
    }
}

public static class Pricing
{
    public sealed record Offer(Wholesaler Wholesaler, int Quantity, int Free, decimal UnitPrice, FreeGoodsCampaign? Campaign)
    {
        public decimal Effective => (Quantity * UnitPrice) / Math.Max(1, Quantity + Free);
    }

    /// <summary>
    /// Depo birim fiyatı (örnek model): satış fiyatından eczacı kârı düşülmüş liste fiyatı × (1 − depo iskontosu).
    /// Depolar ürün bazında da farklı fiyat verir (örnek modelde ürün ve depoya göre sabit ±%4 sapma).
    /// Gerçekte ilaç fiyatlarını fiyat kararnamesi, diğer ürünleri üretici ve depo belirler.
    /// </summary>
    public static decimal UnitPrice(decimal salePrice, ProductCategory category, Wholesaler w, int productId = 0)
    {
        var list = category switch { ProductCategory.Rx => 0.78m, ProductCategory.Otc => 0.72m, _ => 0.66m };
        var spread = productId == 0 ? 0m : (Math.Abs(productId * 7919 + w.Id * 104729) % 9 - 4) / 100m;
        return Math.Round(salePrice * list * (1 - w.DiscountRate) * (1 + spread), 2);
    }

    public static Offer? Best(IEnumerable<Wholesaler> wholesalers, IEnumerable<FreeGoodsCampaign> campaigns, int productId, decimal salePrice,
        ProductCategory category, int quantity)
    {
        Offer? best = null;
        foreach (var w in wholesalers)
        {
            var price = UnitPrice(salePrice, category, w, productId);
            var c = campaigns.FirstOrDefault(x => x.WholesalerId == w.Id && x.ProductId == productId);
            var qty = c is null ? quantity : c.RoundUp(quantity);
            var offer = new Offer(w, qty, c?.FreeFor(qty) ?? 0, price, c);
            if (best is null || offer.Effective < best.Effective || (offer.Effective == best.Effective && offer.Quantity < best.Quantity)) best = offer;
        }
        return best;
    }
}

// ---------- Siparişler ----------

public sealed record OrderItem(int ProductId, int Quantity);

/// <summary>Bir depoya sipariş açar ve verir. Fiyat depo iskontosuyla, mal fazlası geçerli kampanyadan hesaplanır.</summary>
public sealed record CreatePurchaseOrder(int WholesalerId, IReadOnlyList<OrderItem> Items, bool Place = true, string? Note = null) : ICommand<int>;

public sealed class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrder>
{
    public CreatePurchaseOrderValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Siparişe en az bir ürün ekleyin.");
        RuleForEach(x => x.Items).Must(i => i.Quantity is > 0 and <= 5000).WithMessage("Miktar 1-5000 arasında olmalı.");
    }
}

public sealed class CreatePurchaseOrderHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<CreatePurchaseOrder, int>
{
    public async Task<Result<int>> HandleAsync(CreatePurchaseOrder c, CancellationToken cancellationToken)
    {
        var now = time.Now();
        var today = time.Today();
        await using var uow = uowFactory.Create();
        return await uow.ExecuteInTransactionAsync(async ct =>
        {
            var w = await uow.Repository<Wholesaler>().FirstOrDefaultAsync(new Specification<Wholesaler>().Where(x => x.Id == c.WholesalerId).Tracking(), ct)
                    ?? throw new NotFoundException("Depo", c.WholesalerId);
            var ids = c.Items.Select(i => i.ProductId).ToList();
            var products = (await uow.Repository<Product>().ListAsync(new Specification<Product>().Where(p => ids.Contains(p.Id)).Tracking(), ct)).ToDictionary(p => p.Id);
            var campaigns = await uow.Repository<FreeGoodsCampaign>().ListAsync(
                new Specification<FreeGoodsCampaign>().Where(x => x.WholesalerId == w.Id && ids.Contains(x.ProductId) && x.ValidUntil >= today), ct);
            var order = new PurchaseOrder(await NextNumberAsync(uow, now, ct), w, now, c.Note);
            foreach (var item in c.Items)
            {
                var p = products.GetValueOrDefault(item.ProductId) ?? throw new NotFoundException("Ürün", item.ProductId);
                var campaign = campaigns.FirstOrDefault(x => x.ProductId == p.Id);
                order.AddLine(p, item.Quantity, Pricing.UnitPrice(p.SalePrice, p.Category, w, p.Id), campaign?.FreeFor(item.Quantity) ?? 0);
            }
            if (c.Place) order.Place(now);
            uow.Repository<PurchaseOrder>().Add(order);
            await uow.SaveChangesAsync(ct);
            return Result<int>.Success(order.Id);
        }, cancellationToken);
    }

    internal static async Task<string> NextNumberAsync(IUnitOfWork uow, DateTime now, CancellationToken ct)
    {
        var yearStart = new DateTime(now.Year, 1, 1);
        var count = await uow.Repository<PurchaseOrder>().CountAsync(new Specification<PurchaseOrder>().Where(o => o.CreatedOn >= yearStart), ct);
        return $"SP-{now:yy}-{count + 1:00000}";
    }
}

public sealed record PlaceOrder(int OrderId) : ICommand;

public sealed class PlaceOrderHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<PlaceOrder>
{
    public async Task<Result> HandleAsync(PlaceOrder c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var order = await OrderData.LoadAsync(uow, c.OrderId, cancellationToken);
        order.Place(time.Now());
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record CancelOrder(int OrderId) : ICommand;

public sealed class CancelOrderHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<CancelOrder>
{
    public async Task<Result> HandleAsync(CancelOrder c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var order = await OrderData.LoadAsync(uow, c.OrderId, cancellationToken);
        order.Cancel();
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record ReceivedLot(int LineId, string LotNo, DateOnly Expiry);

/// <summary>Teslim alma: her satırın parti no ve SKT'si (karekoddan) ile stok partileri oluşur, vade başlar.</summary>
public sealed record ReceiveOrder(int OrderId, IReadOnlyList<ReceivedLot> Lots) : ICommand<int>;

public sealed class ReceiveOrderHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<ReceiveOrder, int>
{
    public async Task<Result<int>> HandleAsync(ReceiveOrder c, CancellationToken cancellationToken)
    {
        var now = time.Now();
        await using var uow = uowFactory.Create();
        var order = await OrderData.LoadAsync(uow, c.OrderId, cancellationToken);
        var byLine = c.Lots.ToDictionary(l => l.LineId);
        var lots = order.Receive(now, line => byLine.TryGetValue(line.Id, out var l)
            ? (l.LotNo, l.Expiry)
            : throw new DomainException($"{line.Product.Name} için parti no ve SKT girilmedi."));
        uow.Repository<StockLot>().AddRange(lots);
        foreach (var lot in lots) uow.Repository<StockMovement>().Add(new StockMovement(lot, MovementType.Receive, lot.Quantity, now, order.Number));
        await uow.SaveChangesAsync(cancellationToken);
        return lots.Sum(l => l.Quantity);
    }
}

internal static class OrderData
{
    public static async Task<PurchaseOrder> LoadAsync(IUnitOfWork uow, int id, CancellationToken ct)
        => await uow.Repository<PurchaseOrder>().FirstOrDefaultAsync(new Specification<PurchaseOrder>().Where(o => o.Id == id)
               .Include(o => o.Wholesaler).Include(o => o.Lines).Include("Lines.Product").Tracking(), ct)
           ?? throw new NotFoundException("Sipariş", id);
}

public enum OrderFilter { Open, Received, All }

public sealed record OrderRow(int Id, string Number, string Wholesaler, DateTime CreatedOn, DateTime? OrderedAt, DateTime? ReceivedAt, OrderStatus Status,
    int Lines, int Boxes, decimal Total, decimal FreeValue, DateOnly? DueDate)
{
    public string StatusText => Texts.Of(Status);
}

public sealed record ListOrders(OrderFilter Filter = OrderFilter.Open, int? WholesalerId = null) : IQuery<IReadOnlyList<OrderRow>>;

public sealed class ListOrdersHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListOrders, IReadOnlyList<OrderRow>>
{
    public async Task<Result<IReadOnlyList<OrderRow>>> HandleAsync(ListOrders q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var spec = new Specification<PurchaseOrder>().OrderByDescending(o => o.CreatedOn);
        switch (q.Filter)
        {
            case OrderFilter.Open: spec.Where(o => o.Status == OrderStatus.Draft || o.Status == OrderStatus.Ordered); break;
            case OrderFilter.Received:
                var from = time.Today().AddDays(-60).Start();
                spec.Where(o => o.Status == OrderStatus.Received && o.ReceivedAt >= from);
                break;
            default: spec.Page(0, 500); break;
        }
        if (q.WholesalerId is int wid) spec.Where(o => o.WholesalerId == wid);
        var rows = await uow.Repository<PurchaseOrder>().ListAsync(spec, o => new
        {
            o.Id, o.Number, Wholesaler = o.Wholesaler.Name, o.CreatedOn, o.OrderedAt, o.ReceivedAt, o.Status, o.DueDate,
            Lines = o.Lines.Select(l => new { l.Quantity, l.FreeQuantity, l.UnitPrice, l.Product.SalePrice }).ToList(),
        }, cancellationToken);
        return rows.Select(o => new OrderRow(o.Id, o.Number, o.Wholesaler, o.CreatedOn, o.OrderedAt, o.ReceivedAt, o.Status, o.Lines.Count,
            o.Lines.Sum(l => l.Quantity + l.FreeQuantity), o.Lines.Sum(l => l.Quantity * l.UnitPrice), o.Lines.Sum(l => l.FreeQuantity * l.SalePrice),
            o.DueDate)).ToList();
    }
}

public sealed record OrderLineRow(int LineId, int ProductId, string Product, string Barcode, int Quantity, int Free, decimal UnitPrice, decimal LineTotal,
    string LotNo, DateOnly Expiry, bool ColdChain)
{
    public string FreeText => Free > 0 ? $"+{Free}" : "";
}

public sealed record OrderDetail(OrderRow Order, IReadOnlyList<OrderLineRow> Lines);

/// <summary>Sipariş ayrıntısı. Teslim alınmamış satırlarda parti no ve SKT önerisi dolu gelir (karekod okuma yerine örnek).</summary>
public sealed record GetOrder(int OrderId) : IQuery<OrderDetail>;

public sealed class GetOrderHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetOrder, OrderDetail>
{
    public async Task<Result<OrderDetail>> HandleAsync(GetOrder q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var o = await uow.Repository<PurchaseOrder>().FirstOrDefaultAsync(new Specification<PurchaseOrder>().Where(x => x.Id == q.OrderId)
            .Include(x => x.Wholesaler).Include(x => x.Lines).Include("Lines.Product"), cancellationToken) ?? throw new NotFoundException("Sipariş", q.OrderId);
        var row = new OrderRow(o.Id, o.Number, o.Wholesaler.Name, o.CreatedOn, o.OrderedAt, o.ReceivedAt, o.Status, o.Lines.Count, o.Boxes, o.Total,
            o.FreeGoodsValue, o.DueDate);
        var lines = o.Lines.OrderBy(l => l.Product.Name).Select(l => new OrderLineRow(l.Id, l.ProductId, l.Product.Name, l.Product.Barcode, l.Quantity,
            l.FreeQuantity, l.UnitPrice, l.LineTotal,
            l.LotNo ?? SuggestLot(l.Product.Barcode, today), l.Expiry ?? today.AddMonths(18 + l.ProductId % 13), l.Product.ColdChain)).ToList();
        return new OrderDetail(row, lines);
    }

    static string SuggestLot(string barcode, DateOnly today) => $"P{barcode[^4..]}{today:yyMM}";
}
