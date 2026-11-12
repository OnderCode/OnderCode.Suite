using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Eczane.Domain;

/// <summary>
/// Stok partisi: aynı parti numarası ve son kullanma tarihiyle (SKT) gelen kutular. Stok miktarı partilerin toplamıdır;
/// satışta önce SKT'si en yakın parti kullanılır (FEFO). SKT'si geçmiş parti satılamaz.
/// </summary>
public sealed class StockLot : AuditableEntity
{
    private StockLot() { }

    public StockLot(Product product, string lotNo, DateOnly expiry, int quantity, decimal unitCost, DateTime receivedAt, PurchaseOrder? order = null)
    {
        if (string.IsNullOrWhiteSpace(lotNo)) throw new DomainException("Parti numarası boş olamaz.");
        if (quantity <= 0) throw new DomainException("Parti miktarı sıfırdan büyük olmalı.");
        if (unitCost < 0) throw new DomainException("Birim maliyet negatif olamaz.");
        if (DateOnly.FromDateTime(receivedAt) >= expiry) throw new DomainException($"{product.Name}: SKT'si geçmiş ürün stoğa alınamaz.");
        Product = product;
        ProductId = product.Id;
        LotNo = lotNo.Trim().ToUpperInvariant();
        Expiry = expiry;
        InitialQuantity = quantity;
        Quantity = quantity;
        UnitCost = unitCost;
        ReceivedAt = receivedAt;
        if (order is not null)
        {
            PurchaseOrder = order;
            PurchaseOrderId = order.Id;
        }
    }

    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public string LotNo { get; private set; } = "";
    public DateOnly Expiry { get; private set; }
    public int InitialQuantity { get; private set; }

    /// <summary>Kalan kutu.</summary>
    public int Quantity { get; private set; }

    public decimal UnitCost { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public int? PurchaseOrderId { get; private set; }
    public PurchaseOrder? PurchaseOrder { get; private set; }

    public bool IsExpired(DateOnly today) => Expiry <= today;
    public int DaysToExpiry(DateOnly today) => Expiry.DayNumber - today.DayNumber;
    public decimal StockValue => Quantity * UnitCost;

    public void Take(int quantity, DateOnly today)
    {
        if (quantity <= 0) throw new DomainException("Miktar sıfırdan büyük olmalı.");
        if (IsExpired(today)) throw new DomainException($"{Product?.Name} {LotNo}: SKT'si geçmiş parti satılamaz.");
        if (quantity > Quantity) throw new DomainException($"{Product?.Name} {LotNo}: partide {Quantity} kutu var.");
        Quantity -= quantity;
    }

    /// <summary>İade ya da sayım fazlası.</summary>
    public void Put(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Miktar sıfırdan büyük olmalı.");
        Quantity += quantity;
    }

    /// <summary>Sayım farkı ya da imha: kalan miktarı düzeltir, fark döner.</summary>
    public int AdjustTo(int counted)
    {
        if (counted < 0) throw new DomainException("Sayım miktarı negatif olamaz.");
        var diff = counted - Quantity;
        Quantity = counted;
        return diff;
    }
}

public enum MovementType { Receive, Sale, Return, Disposal, CountAdjustment, SupplierReturn }

/// <summary>
/// Stok kartı hareketi (alım, satış, iade, imha, sayım farkı). Satış hareketleri satış satırlarından okunur;
/// bu tabloya yalnızca satış dışı hareketler yazılır.
/// </summary>
public sealed class StockMovement : AuditableEntity
{
    private StockMovement() { }

    public StockMovement(StockLot lot, MovementType type, int quantity, DateTime at, string? reference = null)
    {
        if (quantity == 0) throw new DomainException("Hareket miktarı sıfır olamaz.");
        Lot = lot;
        LotId = lot.Id;
        Product = lot.Product;
        ProductId = lot.ProductId;
        Type = type;
        Quantity = quantity;
        At = at;
        UnitCost = lot.UnitCost;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
    }

    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int LotId { get; private set; }
    public StockLot Lot { get; private set; } = null!;
    public MovementType Type { get; private set; }

    /// <summary>İşaretli miktar: giriş artı, çıkış eksi.</summary>
    public int Quantity { get; private set; }

    public DateTime At { get; private set; }
    public decimal UnitCost { get; private set; }
    public string? Reference { get; private set; }
}

public enum ExpiryLevel { Ok, Watch, Critical, Expired }

public static class StockRules
{
    /// <summary>SKT uyarı eşikleri: 90 gün izleme, 30 gün kritik (depoya iade/kampanya kararı).</summary>
    public const int WatchDays = 90, CriticalDays = 30;

    public static ExpiryLevel LevelOf(DateOnly expiry, DateOnly today)
    {
        var days = expiry.DayNumber - today.DayNumber;
        return days <= 0 ? ExpiryLevel.Expired : days <= CriticalDays ? ExpiryLevel.Critical : days <= WatchDays ? ExpiryLevel.Watch : ExpiryLevel.Ok;
    }

    /// <summary>
    /// FEFO dağıtımı: SKT'si geçmemiş partilerden SKT'si en yakın olandan başlayarak istenen miktarı ayırır.
    /// Yeterli stok yoksa hata verir; hiçbir partiye dokunmaz (yalnızca plan döner).
    /// </summary>
    public static IReadOnlyList<(StockLot Lot, int Quantity)> Allocate(IEnumerable<StockLot> lots, int quantity, DateOnly today)
    {
        if (quantity <= 0) throw new DomainException("Miktar sıfırdan büyük olmalı.");
        var usable = lots.Where(l => l.Quantity > 0 && !l.IsExpired(today)).OrderBy(l => l.Expiry).ThenBy(l => l.ReceivedAt).ToList();
        var available = usable.Sum(l => l.Quantity);
        if (available < quantity)
        {
            var name = usable.FirstOrDefault()?.Product?.Name ?? lots.FirstOrDefault()?.Product?.Name ?? "Ürün";
            throw new DomainException($"{name}: stokta {available} kutu var, {quantity} istendi.");
        }
        var plan = new List<(StockLot, int)>();
        var left = quantity;
        foreach (var lot in usable)
        {
            if (left == 0) break;
            var take = Math.Min(left, lot.Quantity);
            plan.Add((lot, take));
            left -= take;
        }
        return plan;
    }

    /// <summary>
    /// Sipariş önerisi: günlük ortalama satış × (teslim süresi + emniyet günü) hedef stoktur; en az stok seviyesinin altına
    /// düşülmez. Mevcut stok ve yoldaki sipariş düşülür, sonuç yukarı yuvarlanır.
    /// </summary>
    public static int SuggestOrder(double dailyUsage, int onHand, int onOrder, int minStock, double leadDays = 1, double safetyDays = 7)
    {
        var target = Math.Max(minStock, Math.Ceiling(dailyUsage * (leadDays + safetyDays)));
        var need = target - onHand - onOrder;
        return need <= 0 ? 0 : (int)Math.Ceiling(need);
    }
}
