using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Eczane.Domain;

/// <summary>Ecza deposu (toptancı). Günde birkaç kez sevkiyat yapar, alımları vadeli fatura eder.</summary>
public sealed class Wholesaler : Entity
{
    private Wholesaler() { }

    public Wholesaler(string name, int deliveriesPerDay, int paymentTermDays, decimal discountRate)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Depo adı boş olamaz.");
        if (deliveriesPerDay is < 1 or > 6) throw new DomainException("Günlük sevkiyat 1-6 arasında olmalı.");
        if (paymentTermDays is < 0 or > 180) throw new DomainException("Vade 0-180 gün arasında olmalı.");
        if (discountRate is < 0 or >= 0.5m) throw new DomainException("İskonto %0-%50 arasında olmalı.");
        Name = name.Trim();
        DeliveriesPerDay = deliveriesPerDay;
        PaymentTermDays = paymentTermDays;
        DiscountRate = discountRate;
        IsActive = true;
    }

    public string Name { get; private set; } = "";
    public int DeliveriesPerDay { get; private set; }
    public int PaymentTermDays { get; private set; }

    /// <summary>Depo iskontosu (0,08 = %8), satış fiyatı üzerinden.</summary>
    public decimal DiscountRate { get; private set; }

    public bool IsActive { get; private set; }

    public void Change(int deliveriesPerDay, int paymentTermDays, decimal discountRate)
    {
        if (deliveriesPerDay is < 1 or > 6) throw new DomainException("Günlük sevkiyat 1-6 arasında olmalı.");
        if (paymentTermDays is < 0 or > 180) throw new DomainException("Vade 0-180 gün arasında olmalı.");
        if (discountRate is < 0 or >= 0.5m) throw new DomainException("İskonto %0-%50 arasında olmalı.");
        DeliveriesPerDay = deliveriesPerDay;
        PaymentTermDays = paymentTermDays;
        DiscountRate = discountRate;
    }
}

public enum OrderStatus { Draft, Ordered, Received, Cancelled }

/// <summary>
/// Depo siparişi. Taslak → Sipariş verildi → Teslim alındı. Teslim alınırken her satır için parti numarası ve SKT
/// girilir (karekoddan okunur); satırlar stok partisine dönüşür, ödeme vadesi teslim gününden başlar.
/// </summary>
public sealed class PurchaseOrder : AuditableEntity
{
    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder() { }

    public PurchaseOrder(string number, Wholesaler wholesaler, DateTime createdAt, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(number)) throw new DomainException("Sipariş numarası boş olamaz.");
        if (!wholesaler.IsActive) throw new DomainException($"{wholesaler.Name} pasif; sipariş verilemez.");
        Number = number;
        Wholesaler = wholesaler;
        WholesalerId = wholesaler.Id;
        CreatedOn = createdAt;
        Status = OrderStatus.Draft;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public string Number { get; private set; } = "";
    public int WholesalerId { get; private set; }
    public Wholesaler Wholesaler { get; private set; } = null!;
    public DateTime CreatedOn { get; private set; }
    public DateTime? OrderedAt { get; private set; }
    public DateTime? ReceivedAt { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? Note { get; private set; }

    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines;

    public decimal Total => _lines.Sum(l => l.LineTotal);

    /// <summary>Mal fazlasının satış fiyatıyla değeri (bedava gelen kutular).</summary>
    public decimal FreeGoodsValue => _lines.Sum(l => l.FreeQuantity * (l.Product?.SalePrice ?? 0m));

    public int Boxes => _lines.Sum(l => l.Quantity + l.FreeQuantity);

    public PurchaseOrderLine AddLine(Product product, int quantity, decimal unitPrice, int freeQuantity = 0)
    {
        EnsureDraft();
        if (!product.IsActive) throw new DomainException($"{product.Name} pasif ürün.");
        var existing = _lines.FirstOrDefault(l => l.Product == product || (l.ProductId != 0 && l.ProductId == product.Id));
        if (existing is not null)
        {
            existing.Change(existing.Quantity + quantity, unitPrice, existing.FreeQuantity + freeQuantity);
            return existing;
        }
        var line = new PurchaseOrderLine(this, product, quantity, unitPrice, freeQuantity);
        _lines.Add(line);
        return line;
    }

    public void RemoveLine(int productId)
    {
        EnsureDraft();
        _lines.RemoveAll(l => l.ProductId == productId);
    }

    public void Place(DateTime at)
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new DomainException("Boş sipariş verilemez.");
        Status = OrderStatus.Ordered;
        OrderedAt = at;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Received) throw new DomainException("Teslim alınmış sipariş iptal edilemez; depoya iade girin.");
        Status = OrderStatus.Cancelled;
    }

    /// <summary>
    /// Teslim alma. <paramref name="lotOf"/> her satır için parti no ve SKT döner. Mal fazlası aynı partiye eklenir;
    /// birim maliyet (tutar / toplam kutu) ürün kartının alış fiyatı olur.
    /// </summary>
    public IReadOnlyList<StockLot> Receive(DateTime at, Func<PurchaseOrderLine, (string LotNo, DateOnly Expiry)> lotOf)
    {
        if (Status != OrderStatus.Ordered) throw new DomainException("Yalnızca verilmiş sipariş teslim alınabilir.");
        var lots = new List<StockLot>();
        foreach (var line in _lines)
        {
            var (lotNo, expiry) = lotOf(line);
            var lot = new StockLot(line.Product, lotNo, expiry, line.Quantity + line.FreeQuantity, line.EffectiveUnitCost, at, this);
            line.Product.UpdateCost(line.EffectiveUnitCost);
            line.Received(lotNo, expiry);
            lots.Add(lot);
        }
        Status = OrderStatus.Received;
        ReceivedAt = at;
        DueDate = DateOnly.FromDateTime(at).AddDays(Wholesaler.PaymentTermDays);
        return lots;
    }

    void EnsureDraft()
    {
        if (Status != OrderStatus.Draft) throw new DomainException("Yalnızca taslak sipariş değiştirilebilir.");
    }
}

public sealed class PurchaseOrderLine : Entity
{
    private PurchaseOrderLine() { }

    internal PurchaseOrderLine(PurchaseOrder order, Product product, int quantity, decimal unitPrice, int freeQuantity)
    {
        Order = order;
        Product = product;
        ProductId = product.Id;
        Change(quantity, unitPrice, freeQuantity);
    }

    public int OrderId { get; private set; }
    public PurchaseOrder Order { get; private set; } = null!;
    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int Quantity { get; private set; }

    /// <summary>Mal fazlası (MF): "10+1" kampanyasında 1.</summary>
    public int FreeQuantity { get; private set; }

    /// <summary>İskonto düşülmüş birim alış fiyatı (KDV dahil).</summary>
    public decimal UnitPrice { get; private set; }

    public string? LotNo { get; private set; }
    public DateOnly? Expiry { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;

    /// <summary>Mal fazlası dağıtılmış gerçek birim maliyet.</summary>
    public decimal EffectiveUnitCost => Quantity + FreeQuantity == 0 ? 0 : Math.Round(LineTotal / (Quantity + FreeQuantity), 4);

    public string FreeText => FreeQuantity > 0 ? $"{Quantity}+{FreeQuantity}" : "";

    internal void Change(int quantity, decimal unitPrice, int freeQuantity)
    {
        if (quantity is < 1 or > 5000) throw new DomainException("Miktar 1-5000 arasında olmalı.");
        if (freeQuantity < 0 || freeQuantity > quantity) throw new DomainException("Mal fazlası 0 ile sipariş miktarı arasında olmalı.");
        if (unitPrice < 0) throw new DomainException("Birim fiyat negatif olamaz.");
        Quantity = quantity;
        UnitPrice = unitPrice;
        FreeQuantity = freeQuantity;
    }

    internal void Received(string lotNo, DateOnly expiry)
    {
        LotNo = lotNo;
        Expiry = expiry;
    }
}

/// <summary>Kampanya kuralları: belirli miktara mal fazlası ("10+1", "20+3").</summary>
public static class FreeGoods
{
    /// <summary>"10+1" kuralına göre verilen miktar için bedava kutu.</summary>
    public static int For(int quantity, int threshold, int free) => threshold <= 0 ? 0 : quantity / threshold * free;
}

public enum SupplierPaymentMethod { Transfer, Check, Card }

/// <summary>Depoya yapılan ödeme. Cari bakiye: teslim alınan siparişler − ödemeler.</summary>
public sealed class SupplierPayment : AuditableEntity
{
    private SupplierPayment() { }

    public SupplierPayment(Wholesaler wholesaler, DateOnly date, decimal amount, SupplierPaymentMethod method, string? note = null)
    {
        if (amount <= 0) throw new DomainException("Ödeme tutarı sıfırdan büyük olmalı.");
        Wholesaler = wholesaler;
        WholesalerId = wholesaler.Id;
        Date = date;
        Amount = amount;
        Method = method;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public int WholesalerId { get; private set; }
    public Wholesaler Wholesaler { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public decimal Amount { get; private set; }
    public SupplierPaymentMethod Method { get; private set; }
    public string? Note { get; private set; }
}

/// <summary>Deponun ürün bazlı mal fazlası kampanyası: "10 alana 1 bedava" (eşik 10, bedava 1), geçerlilik tarihine kadar.</summary>
public sealed class FreeGoodsCampaign : Entity
{
    private FreeGoodsCampaign() { }

    public FreeGoodsCampaign(Wholesaler wholesaler, Product product, int threshold, int free, DateOnly validUntil)
    {
        if (threshold is < 2 or > 500) throw new DomainException("Kampanya eşiği 2-500 arasında olmalı.");
        if (free < 1 || free >= threshold) throw new DomainException("Bedava miktarı 1 ile eşik arasında olmalı.");
        Wholesaler = wholesaler;
        WholesalerId = wholesaler.Id;
        Product = product;
        ProductId = product.Id;
        Threshold = threshold;
        Free = free;
        ValidUntil = validUntil;
    }

    public int WholesalerId { get; private set; }
    public Wholesaler Wholesaler { get; private set; } = null!;
    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int Threshold { get; private set; }
    public int Free { get; private set; }
    public DateOnly ValidUntil { get; private set; }

    public string Text => $"{Threshold}+{Free}";

    public bool IsValid(DateOnly today) => today <= ValidUntil;

    public int FreeFor(int quantity) => FreeGoods.For(quantity, Threshold, Free);

    /// <summary>Kampanyadan yararlanmak için miktarı eşiğe yuvarlar (en fazla %30 fazlasıyla).</summary>
    public int RoundUp(int quantity)
    {
        var rounded = (int)Math.Ceiling(quantity / (double)Threshold) * Threshold;
        return rounded <= quantity * 1.3 ? rounded : quantity;
    }
}
