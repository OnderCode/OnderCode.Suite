using Eczane.Domain;
using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace Eczane.Application;

// ---------- Ürün listesi ----------

public enum StockFilter { All, Low, Out, Expiring }

public sealed record ProductRow(
    int Id, string Barcode, string Name, ProductCategory Category, string? Group, PrescriptionColor Color, bool ColdChain, bool Reimbursed,
    int Stock, int MinStock, DateOnly? NearestExpiry, ExpiryLevel ExpiryLevel, decimal SalePrice, decimal PurchasePrice,
    decimal StockValue, int Sold30, string? Shelf, bool IsActive)
{
    public string CategoryText => Texts.Of(Category);
    public string ColorText => Category == ProductCategory.Rx ? Texts.Of(Color) : "";
    public double Margin => SalePrice <= 0 ? 0 : (double)((SalePrice - PurchasePrice) / SalePrice);
    public double DailyUsage => Sold30 / 30.0;

    /// <summary>Mevcut stok bugünkü satış hızıyla kaç gün yeter.</summary>
    public int? DaysOfCover => DailyUsage <= 0 ? null : (int)Math.Floor(Stock / DailyUsage);

    public string StockStatus => Stock == 0 ? "Stokta yok" : Stock <= MinStock ? "Kritik stok" : "Yeterli";
    public string ExpiryText => NearestExpiry is null ? "" : Texts.Of(ExpiryLevel);
}

public sealed record ListProducts(string? Search = null, ProductCategory? Category = null, StockFilter Filter = StockFilter.All, bool IncludeInactive = false)
    : IQuery<IReadOnlyList<ProductRow>>;

public sealed class ListProductsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListProducts, IReadOnlyList<ProductRow>>
{
    public async Task<Result<IReadOnlyList<ProductRow>>> HandleAsync(ListProducts q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var spec = new Specification<Product>().OrderBy(p => p.Name).Search(q.Search, p => p.Name, p => p.Barcode, p => p.ActiveIngredient, p => p.Group);
        if (!q.IncludeInactive) spec.Where(p => p.IsActive);
        if (q.Category is { } c) spec.Where(p => p.Category == c);
        var products = await uow.Repository<Product>().ListAsync(spec, cancellationToken);
        var stock = await StockQueries.OnHandAsync(uow, today, cancellationToken);
        var sold = await StockQueries.SoldAsync(uow, today.AddDays(-30).Start(), today.AddDays(1).Start(), cancellationToken);

        var rows = products.Select(p =>
        {
            var s = stock.GetValueOrDefault(p.Id);
            return new ProductRow(p.Id, p.Barcode, p.Name, p.Category, p.Group, p.Color, p.ColdChain, p.Reimbursed,
                s?.Quantity ?? 0, p.MinStock, s?.NearestExpiry, s?.NearestExpiry is { } e ? StockRules.LevelOf(e, today) : ExpiryLevel.Ok,
                p.SalePrice, p.PurchasePrice, s?.Value ?? 0m, sold.GetValueOrDefault(p.Id), p.Shelf, p.IsActive);
        });
        rows = q.Filter switch
        {
            StockFilter.Low => rows.Where(r => r.Stock <= r.MinStock),
            StockFilter.Out => rows.Where(r => r.Stock == 0),
            StockFilter.Expiring => rows.Where(r => r.ExpiryLevel != ExpiryLevel.Ok && r.NearestExpiry is not null),
            _ => rows,
        };
        return rows.ToList();
    }
}

/// <summary>Stok ve satış toplamları için paylaşılan sorgular.</summary>
internal static class StockQueries
{
    public sealed record OnHand(int Quantity, decimal Value, DateOnly? NearestExpiry);

    /// <summary>Ürün başına satılabilir stok (SKT'si geçmemiş partiler), değer ve en yakın SKT.</summary>
    public static async Task<Dictionary<int, OnHand>> OnHandAsync(IUnitOfWork uow, DateOnly today, CancellationToken ct)
    {
        var lots = await uow.Repository<StockLot>().ListAsync(
            new Specification<StockLot>().Where(l => l.Quantity > 0 && l.Expiry > today),
            l => new { l.ProductId, l.Quantity, l.UnitCost, l.Expiry }, ct);
        return lots.GroupBy(l => l.ProductId).ToDictionary(g => g.Key,
            g => new OnHand(g.Sum(l => l.Quantity), g.Sum(l => l.Quantity * l.UnitCost), g.Min(l => l.Expiry)));
    }

    /// <summary>Ürün başına satılan kutu (iade edilenler hariç).</summary>
    public static async Task<Dictionary<int, int>> SoldAsync(IUnitOfWork uow, DateTime from, DateTime to, CancellationToken ct)
    {
        var lines = await uow.Repository<SaleLine>().ListAsync(
            new Specification<SaleLine>().Where(l => l.Sale.SoldAt >= from && l.Sale.SoldAt < to && l.Sale.Status == SaleStatus.Completed),
            l => new { l.ProductId, l.Quantity }, ct);
        return lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
    }

    /// <summary>Yolda (verilmiş, teslim alınmamış) siparişlerdeki kutular.</summary>
    public static async Task<Dictionary<int, int>> OnOrderAsync(IUnitOfWork uow, CancellationToken ct)
    {
        var lines = await uow.Repository<PurchaseOrderLine>().ListAsync(
            new Specification<PurchaseOrderLine>().Where(l => l.Order.Status == OrderStatus.Ordered || l.Order.Status == OrderStatus.Draft),
            l => new { l.ProductId, l.Quantity, l.FreeQuantity }, ct);
        return lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity + l.FreeQuantity));
    }
}

// ---------- Ürün kartı ----------

public sealed record LotRow(int LotId, string LotNo, DateOnly Expiry, int Quantity, int InitialQuantity, decimal UnitCost, DateTime ReceivedAt,
    string? Supplier, ExpiryLevel Level)
{
    public string LevelText => Texts.Of(Level);
    public decimal Value => Quantity * UnitCost;
}

public sealed record CardMovement(DateTime At, string Type, string Reference, int In, int Out, decimal UnitValue);

public sealed record SupplierPrice(string Supplier, DateTime ReceivedAt, decimal UnitPrice, int Quantity, int Free, decimal EffectiveCost);

public sealed record ProductCard(
    int Id, string Barcode, string Name, ProductCategory Category, string? ActiveIngredient, string? Group, PrescriptionColor Color,
    bool ColdChain, bool Reimbursed, decimal SalePrice, decimal PurchasePrice, int MinStock, string? Shelf, bool IsActive,
    IReadOnlyList<LotRow> Lots, IReadOnlyList<CardMovement> Movements, IReadOnlyList<SupplierPrice> Purchases,
    string[] DayLabels, double[] DailySales, int Stock, int Sold30, int Sold90, decimal Revenue90)
{
    public string CategoryText => Texts.Of(Category);
    public double Margin => SalePrice <= 0 ? 0 : (double)((SalePrice - PurchasePrice) / SalePrice);
}

public sealed record GetProductCard(int ProductId) : IQuery<ProductCard>;

public sealed class GetProductCardHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetProductCard, ProductCard>
{
    public async Task<Result<ProductCard>> HandleAsync(GetProductCard q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var p = await uow.Repository<Product>().GetRequiredAsync(q.ProductId, cancellationToken);
        var lots = await uow.Repository<StockLot>().ListAsync(
            new Specification<StockLot>().Where(l => l.ProductId == p.Id).Include(l => l.PurchaseOrder).Include("PurchaseOrder.Wholesaler").Include("PurchaseOrder.Lines").OrderByDescending(l => l.ReceivedAt),
            cancellationToken);
        var from = today.AddDays(-89).Start();
        var sales = await uow.Repository<SaleLine>().ListAsync(
            new Specification<SaleLine>().Where(l => l.ProductId == p.Id && l.Sale.SoldAt >= from && l.Sale.Status == SaleStatus.Completed),
            l => new { l.Sale.SoldAt, l.Sale.Number, l.Sale.Kind, l.Quantity, l.UnitPrice }, cancellationToken);
        var moves = await uow.Repository<StockMovement>().ListAsync(
            new Specification<StockMovement>().Where(m => m.ProductId == p.Id && m.At >= from),
            m => new { m.At, m.Type, m.Quantity, m.Reference, m.UnitCost }, cancellationToken);

        var card = new List<CardMovement>();
        foreach (var l in lots.Where(l => l.ReceivedAt >= from))
            card.Add(new CardMovement(l.ReceivedAt, Texts.Of(MovementType.Receive), $"{l.PurchaseOrder?.Wholesaler?.Name ?? "Açılış"} · {l.LotNo}", l.InitialQuantity, 0, l.UnitCost));
        foreach (var s in sales)
            card.Add(new CardMovement(s.SoldAt, s.Kind == SaleKind.Prescription ? "Reçete" : "Serbest satış", s.Number, 0, s.Quantity, s.UnitPrice));
        foreach (var m in moves)
            card.Add(new CardMovement(m.At, Texts.Of(m.Type), m.Reference ?? "", Math.Max(0, m.Quantity), Math.Max(0, -m.Quantity), m.UnitCost));

        var labels = new string[60];
        var daily = new double[60];
        var start60 = today.AddDays(-59);
        for (var i = 0; i < 60; i++) labels[i] = start60.AddDays(i).ToString("dd.MM");
        foreach (var s in sales)
        {
            var idx = DateOnly.FromDateTime(s.SoldAt).DayNumber - start60.DayNumber;
            if (idx is >= 0 and < 60) daily[idx] += s.Quantity;
        }
        var purchases = lots.Where(l => l.PurchaseOrder is not null).Take(12)
            .Select(l =>
            {
                var line = l.PurchaseOrder!.Lines.FirstOrDefault(x => x.ProductId == p.Id);
                return new SupplierPrice(l.PurchaseOrder.Wholesaler?.Name ?? "", l.ReceivedAt, line?.UnitPrice ?? l.UnitCost, line?.Quantity ?? l.InitialQuantity,
                    line?.FreeQuantity ?? 0, l.UnitCost);
            }).ToList();

        var thirty = today.AddDays(-29).Start();
        return new ProductCard(p.Id, p.Barcode, p.Name, p.Category, p.ActiveIngredient, p.Group, p.Color, p.ColdChain, p.Reimbursed,
            p.SalePrice, p.PurchasePrice, p.MinStock, p.Shelf, p.IsActive,
            lots.Where(l => l.Quantity > 0).OrderBy(l => l.Expiry)
                .Select(l => new LotRow(l.Id, l.LotNo, l.Expiry, l.Quantity, l.InitialQuantity, l.UnitCost, l.ReceivedAt, l.PurchaseOrder?.Wholesaler?.Name,
                    StockRules.LevelOf(l.Expiry, today))).ToList(),
            card.OrderByDescending(c => c.At).ToList(), purchases, labels, daily,
            lots.Where(l => l.Expiry > today).Sum(l => l.Quantity),
            sales.Where(s => s.SoldAt >= thirty).Sum(s => s.Quantity), sales.Sum(s => s.Quantity), sales.Sum(s => s.Quantity * s.UnitPrice));
    }
}

// ---------- Ürün kaydet ----------

public sealed record SaveProduct(
    int? Id, string Barcode, string Name, ProductCategory Category, string? ActiveIngredient, string? Group, PrescriptionColor Color,
    bool ColdChain, bool Reimbursed, decimal SalePrice, decimal PurchasePrice, int MinStock, string? Shelf) : ICommand<int>;

public sealed class SaveProductValidator : AbstractValidator<SaveProduct>
{
    public SaveProductValidator()
    {
        RuleFor(x => x.Barcode).NotEmpty().Must(Ean13.IsValid).WithMessage("Geçerli bir EAN-13 barkodu girin (13 hane, kontrol hanesi doğru).").WithName("Barkod");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150).WithName("Ürün adı");
        RuleFor(x => x.ActiveIngredient).MaximumLength(120).WithName("Etken madde");
        RuleFor(x => x.Group).MaximumLength(60).WithName("Grup");
        RuleFor(x => x.SalePrice).GreaterThan(0).WithName("Satış fiyatı");
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0).WithName("Alış fiyatı");
        RuleFor(x => x.PurchasePrice).LessThanOrEqualTo(x => x.SalePrice).WithMessage("Alış fiyatı satış fiyatından yüksek olamaz.");
        RuleFor(x => x.MinStock).InclusiveBetween(0, 10_000).WithName("En az stok");
        RuleFor(x => x.Shelf).MaximumLength(12).WithName("Raf");
    }
}

public sealed class SaveProductHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<SaveProduct, int>
{
    public async Task<Result<int>> HandleAsync(SaveProduct c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var repo = uow.Repository<Product>();
        var clash = await repo.AnyAsync(new Specification<Product>().Where(p => p.Barcode == c.Barcode && p.Id != (c.Id ?? 0)), cancellationToken);
        if (clash) throw new DomainException($"{c.Barcode} barkodu başka bir üründe kayıtlı.");
        Product product;
        if (c.Id is int id)
        {
            product = await repo.GetRequiredAsync(id, cancellationToken);
            if (product.Barcode != c.Barcode) throw new DomainException("Barkod değiştirilemez; yeni ürün kartı açın.");
            product.Rename(c.Name);
            product.Reprice(c.SalePrice, c.PurchasePrice);
            product.SetMinStock(c.MinStock);
            product.MoveTo(c.Shelf);
        }
        else
        {
            product = new Product(c.Barcode, c.Name, c.Category, c.SalePrice, c.PurchasePrice, c.ActiveIngredient, c.Group, c.Color,
                c.ColdChain, c.Reimbursed, c.MinStock, c.Shelf);
            repo.Add(product);
        }
        await uow.SaveChangesAsync(cancellationToken);
        return product.Id;
    }
}

/// <summary>Satış ekranı için aktif ürünler, satılabilir stok ve fiyat.</summary>
public sealed record CatalogItem(int Id, string Barcode, string Name, ProductCategory Category, PrescriptionColor Color, decimal Price, int Stock,
    bool Reimbursed, string? Shelf)
{
    public string Display => $"{Name}  ·  {Price:N2} ₺  ·  stok {Stock}";
    public string CategoryText => Texts.Of(Category);
    public bool IsRx => Category == ProductCategory.Rx;
}

public sealed record GetSaleCatalog : IQuery<IReadOnlyList<CatalogItem>>;

public sealed class GetSaleCatalogHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetSaleCatalog, IReadOnlyList<CatalogItem>>
{
    public async Task<Result<IReadOnlyList<CatalogItem>>> HandleAsync(GetSaleCatalog q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var products = await uow.Repository<Product>().ListAsync(new Specification<Product>().Where(p => p.IsActive).OrderBy(p => p.Name),
            p => new { p.Id, p.Barcode, p.Name, p.Category, p.Color, p.SalePrice, p.Reimbursed, p.Shelf }, cancellationToken);
        var stock = await StockQueries.OnHandAsync(uow, time.Today(), cancellationToken);
        return products.Select(p => new CatalogItem(p.Id, p.Barcode, p.Name, p.Category, p.Color, p.SalePrice, stock.GetValueOrDefault(p.Id)?.Quantity ?? 0,
            p.Reimbursed, p.Shelf)).ToList();
    }
}

// ---------- SKT takibi, imha, sayım ----------

public sealed record ExpiryRow(int LotId, int ProductId, string Product, string Barcode, string LotNo, DateOnly Expiry, int DaysLeft, int Quantity,
    decimal UnitCost, ExpiryLevel Level, string? Supplier, double DailyUsage)
{
    public string LevelText => Texts.Of(Level);
    public decimal Value => Quantity * UnitCost;

    /// <summary>Bugünkü satış hızıyla partinin bitmesi için gereken gün.</summary>
    public int? DaysToSellOut => DailyUsage <= 0 ? null : (int)Math.Ceiling(Quantity / DailyUsage);

    /// <summary>Kaç kutu SKT'den önce satılamayacak (tahmini).</summary>
    public int AtRisk => Level == ExpiryLevel.Expired ? Quantity : Math.Max(0, Quantity - (int)Math.Floor(DailyUsage * Math.Max(0, DaysLeft)));

    public string Advice => Level == ExpiryLevel.Expired ? "İmha / depoya iade"
        : AtRisk == 0 ? "Satış hızıyla biter"
        : Level == ExpiryLevel.Critical ? $"{AtRisk} kutu kalır: depoya iade"
        : $"{AtRisk} kutu kalabilir: öne alın";
}

public sealed record ListExpiry(int WithinDays = StockRules.WatchDays) : IQuery<IReadOnlyList<ExpiryRow>>;

public sealed class ListExpiryHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListExpiry, IReadOnlyList<ExpiryRow>>
{
    public async Task<Result<IReadOnlyList<ExpiryRow>>> HandleAsync(ListExpiry q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var limit = today.AddDays(q.WithinDays);
        await using var uow = uowFactory.Create();
        var lots = await uow.Repository<StockLot>().ListAsync(
            new Specification<StockLot>().Where(l => l.Quantity > 0 && l.Expiry <= limit).OrderBy(l => l.Expiry),
            l => new { l.Id, l.ProductId, l.Product.Name, l.Product.Barcode, l.LotNo, l.Expiry, l.Quantity, l.UnitCost, Supplier = (string?)l.PurchaseOrder!.Wholesaler.Name },
            cancellationToken);
        var sold = await StockQueries.SoldAsync(uow, today.AddDays(-30).Start(), today.AddDays(1).Start(), cancellationToken);
        return lots.Select(l => new ExpiryRow(l.Id, l.ProductId, l.Name, l.Barcode, l.LotNo, l.Expiry, l.Expiry.DayNumber - today.DayNumber, l.Quantity,
            l.UnitCost, StockRules.LevelOf(l.Expiry, today), l.Supplier, sold.GetValueOrDefault(l.ProductId) / 30.0)).ToList();
    }
}

/// <summary>Partiden imha (SKT geçmiş, kırık, bozuk). Stoktan düşer, imha hareketi yazılır.</summary>
public sealed record DisposeLot(int LotId, int Quantity, string Reason) : ICommand;

public sealed class DisposeLotValidator : AbstractValidator<DisposeLot>
{
    public DisposeLotValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0).WithName("Miktar");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(150).WithName("Gerekçe");
    }
}

public sealed class DisposeLotHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<DisposeLot>
{
    public async Task<Result> HandleAsync(DisposeLot c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var lot = await uow.Repository<StockLot>().FirstOrDefaultAsync(new Specification<StockLot>().Where(l => l.Id == c.LotId).Include(l => l.Product).Tracking(), cancellationToken)
                  ?? throw new NotFoundException("Parti", c.LotId);
        if (c.Quantity > lot.Quantity) throw new DomainException($"Partide {lot.Quantity} kutu var.");
        lot.AdjustTo(lot.Quantity - c.Quantity);
        uow.Repository<StockMovement>().Add(new StockMovement(lot, MovementType.Disposal, -c.Quantity, time.Now(), c.Reason));
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Sayım: partideki sayılan miktar girilir, fark sayım hareketi olarak yazılır.</summary>
public sealed record CountLot(int LotId, int Counted) : ICommand<int>;

public sealed class CountLotHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<CountLot, int>
{
    public async Task<Result<int>> HandleAsync(CountLot c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var lot = await uow.Repository<StockLot>().FirstOrDefaultAsync(new Specification<StockLot>().Where(l => l.Id == c.LotId).Include(l => l.Product).Tracking(), cancellationToken)
                  ?? throw new NotFoundException("Parti", c.LotId);
        var diff = lot.AdjustTo(c.Counted);
        if (diff != 0) uow.Repository<StockMovement>().Add(new StockMovement(lot, MovementType.CountAdjustment, diff, time.Now(), "Sayım"));
        await uow.SaveChangesAsync(cancellationToken);
        return diff;
    }
}
