using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Eczane.Domain;

/// <summary>Ürün grubu. Reçeteli ilaç yalnızca reçeteyle verilir.</summary>
public enum ProductCategory { Rx, Otc, Dermo, Supplement, Medical, Baby }

/// <summary>
/// Reçete türü (renk). Beyaz normal reçete; kırmızı ve yeşil reçete kontrole tabi ilaçlar; mor ve turuncu reçete
/// özel izlem gerektiren ilaçlar içindir.
/// </summary>
public enum PrescriptionColor { White, Red, Green, Purple, Orange }

/// <summary>
/// Ürün kartı. Fiyatlar KDV dahildir. Barkod EAN-13; örnek veride GS1'in mağaza içi kullanıma ayırdığı 2 ile başlayan
/// aralık kullanılır, gerçek bir ürünün barkoduyla çakışmaz. İlaç adları etken madde, doz ve formdur; marka adı kullanılmaz.
/// </summary>
public sealed class Product : FullAuditableEntity
{
    private Product() { }

    public Product(string barcode, string name, ProductCategory category, decimal salePrice, decimal purchasePrice,
        string? activeIngredient = null, string? group = null, PrescriptionColor color = PrescriptionColor.White,
        bool coldChain = false, bool reimbursed = false, int minStock = 2, string? shelf = null)
    {
        if (!Ean13.IsValid(barcode)) throw new DomainException($"'{barcode}' geçerli bir EAN-13 barkodu değil.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Ürün adı boş olamaz.");
        Barcode = barcode;
        Name = name.Trim();
        Category = category;
        ActiveIngredient = string.IsNullOrWhiteSpace(activeIngredient) ? null : activeIngredient.Trim();
        Group = string.IsNullOrWhiteSpace(group) ? null : group.Trim();
        Color = category == ProductCategory.Rx ? color : PrescriptionColor.White;
        ColdChain = coldChain;
        Reimbursed = category == ProductCategory.Rx && reimbursed;
        Reprice(salePrice, purchasePrice);
        SetMinStock(minStock);
        Shelf = string.IsNullOrWhiteSpace(shelf) ? null : shelf.Trim().ToUpperInvariant();
        IsActive = true;
    }

    public string Barcode { get; private set; } = "";
    public string Name { get; private set; } = "";
    public ProductCategory Category { get; private set; }
    public string? ActiveIngredient { get; private set; }

    /// <summary>Tedavi grubu (Analjezik, Antibiyotik, Kardiyovasküler...) ya da ürün grubu.</summary>
    public string? Group { get; private set; }

    public PrescriptionColor Color { get; private set; }
    public bool ColdChain { get; private set; }

    /// <summary>SGK geri ödeme listesinde mi? Listede olmayan reçeteli ilacın bedelini hasta öder.</summary>
    public bool Reimbursed { get; private set; }

    /// <summary>Perakende satış fiyatı (KDV dahil).</summary>
    public decimal SalePrice { get; private set; }

    /// <summary>Son alış birim maliyeti (KDV dahil, iskonto ve mal fazlası düşülmüş).</summary>
    public decimal PurchasePrice { get; private set; }

    public int MinStock { get; private set; }
    public string? Shelf { get; private set; }
    public bool IsActive { get; private set; }

    public bool RequiresPrescription => Category == ProductCategory.Rx;

    /// <summary>Brüt kâr marjı (satışa göre).</summary>
    public double Margin => SalePrice <= 0 ? 0 : (double)((SalePrice - PurchasePrice) / SalePrice);

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Ürün adı boş olamaz.");
        Name = name.Trim();
    }

    public void Reprice(decimal salePrice, decimal purchasePrice)
    {
        if (salePrice <= 0) throw new DomainException("Satış fiyatı sıfırdan büyük olmalı.");
        if (purchasePrice < 0) throw new DomainException("Alış fiyatı negatif olamaz.");
        SalePrice = salePrice;
        PurchasePrice = purchasePrice;
    }

    internal void UpdateCost(decimal unitCost)
    {
        if (unitCost > 0) PurchasePrice = Math.Round(unitCost, 2);
    }

    public void SetMinStock(int minStock)
    {
        if (minStock is < 0 or > 10_000) throw new DomainException("En az stok 0-10.000 arasında olmalı.");
        MinStock = minStock;
    }

    public void MoveTo(string? shelf) => Shelf = string.IsNullOrWhiteSpace(shelf) ? null : shelf.Trim().ToUpperInvariant();

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}

/// <summary>EAN-13 barkod doğrulama ve kontrol hanesi.</summary>
public static class Ean13
{
    public static bool IsValid(string? code)
        => code is { Length: 13 } && code.All(char.IsAsciiDigit) && code[12] - '0' == CheckDigit(code[..12]);

    /// <summary>12 haneye kontrol hanesini ekler.</summary>
    public static string Complete(string first12)
    {
        if (first12 is not { Length: 12 } || !first12.All(char.IsAsciiDigit)) throw new ArgumentException("12 hane rakam gerekli.", nameof(first12));
        return first12 + CheckDigit(first12);
    }

    static int CheckDigit(string first12)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (first12[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }
}
