using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Eczane.Domain;

public enum SaleKind { Prescription, Retail }

public enum SaleStatus { Open, Completed, Returned }

public enum PaymentType { Cash, Card }

/// <summary>
/// Satış: reçete karşılama ya da serbest (reçetesiz) satış. Satırlar parti bazındadır; bir ürün iki partiden karşılanırsa
/// iki satır olur. Reçeteli ilaç yalnızca reçeteyle, kırmızı/yeşil/mor/turuncu reçeteye tabi ilaç yalnızca o renkte reçeteyle verilir.
/// Tamamlanınca hasta payı ve kurum payı (SGK ya da özel sigorta) hesaplanır.
/// </summary>
public sealed class Sale : AuditableEntity
{
    private readonly List<SaleLine> _lines = [];

    private Sale() { }

    private Sale(string number, SaleKind kind, DateTime at, Employee? soldBy)
    {
        if (string.IsNullOrWhiteSpace(number)) throw new DomainException("Satış numarası boş olamaz.");
        Number = number;
        Kind = kind;
        SoldAt = at;
        Status = SaleStatus.Open;
        Insurance = InsuranceType.None;
        if (soldBy is not null)
        {
            SoldBy = soldBy;
            SoldById = soldBy.Id;
        }
    }

    public static Sale Retail(string number, DateTime at, Employee? soldBy = null, Patient? patient = null)
    {
        var s = new Sale(number, SaleKind.Retail, at, soldBy);
        if (patient is not null)
        {
            s.Patient = patient;
            s.PatientId = patient.Id;
        }
        return s;
    }

    public static Sale Prescription(string number, DateTime at, Patient patient, Hospital hospital, string branch,
        PrescriptionColor color, string prescriptionNo, DateOnly prescribedOn, Employee? soldBy = null)
    {
        if (string.IsNullOrWhiteSpace(prescriptionNo)) throw new DomainException("Reçete numarası boş olamaz.");
        if (string.IsNullOrWhiteSpace(branch)) throw new DomainException("Hekim branşı boş olamaz.");
        var day = DateOnly.FromDateTime(at);
        if (prescribedOn > day) throw new DomainException("Reçete tarihi ileri bir gün olamaz.");
        if (day.DayNumber - prescribedOn.DayNumber > ContributionRules.ValidityDays(color))
            throw new DomainException($"Reçete süresi dolmuş ({ContributionRules.ValidityDays(color)} gün geçerli).");
        var s = new Sale(number, SaleKind.Prescription, at, soldBy)
        {
            Patient = patient,
            PatientId = patient.Id,
            Hospital = hospital,
            HospitalId = hospital.Id,
            Branch = branch.Trim(),
            Color = color,
            PrescriptionNo = prescriptionNo.Trim().ToUpperInvariant(),
            PrescribedOn = prescribedOn,
            Insurance = patient.Insurance,
        };
        return s;
    }

    public string Number { get; private set; } = "";
    public SaleKind Kind { get; private set; }
    public SaleStatus Status { get; private set; }
    public DateTime SoldAt { get; private set; }

    public int? PatientId { get; private set; }
    public Patient? Patient { get; private set; }
    public int? HospitalId { get; private set; }
    public Hospital? Hospital { get; private set; }
    public string? Branch { get; private set; }
    public PrescriptionColor? Color { get; private set; }
    public string? PrescriptionNo { get; private set; }
    public DateOnly? PrescribedOn { get; private set; }
    public InsuranceType Insurance { get; private set; }

    public int? SoldById { get; private set; }
    public Employee? SoldBy { get; private set; }

    /// <summary>Satış anındaki fiyatlarla toplam (KDV dahil).</summary>
    public decimal Total { get; private set; }

    /// <summary>Kasadan tahsil edilen (hasta payı ya da serbest satış tutarı, indirim düşülmüş).</summary>
    public decimal PatientShare { get; private set; }

    /// <summary>Kuruma fatura edilecek (SGK ya da özel sigorta).</summary>
    public decimal InstitutionShare { get; private set; }

    /// <summary>Serbest satışta yapılan indirim.</summary>
    public decimal Discount { get; private set; }

    public decimal Cost { get; private set; }
    public PaymentType? Payment { get; private set; }
    public DateTime? ReturnedAt { get; private set; }

    public IReadOnlyCollection<SaleLine> Lines => _lines;

    /// <summary>Net ciro: toplam − indirim (hasta payı + kurum payı).</summary>
    public decimal NetRevenue => PatientShare + InstitutionShare;

    public decimal GrossProfit => NetRevenue - Cost;

    public int Boxes => _lines.Sum(l => l.Quantity);

    public SaleLine AddLine(Product product, StockLot lot, int quantity, DateOnly today)
    {
        if (Status != SaleStatus.Open) throw new DomainException("Tamamlanmış satışa ürün eklenemez.");
        if (lot.Product != product && lot.ProductId != product.Id) throw new DomainException("Parti bu ürüne ait değil.");
        if (!product.IsActive) throw new DomainException($"{product.Name} pasif ürün.");
        if (product.RequiresPrescription && Kind != SaleKind.Prescription)
            throw new DomainException($"{product.Name} reçeteli ilaç; serbest satışla verilemez.");
        if (Kind == SaleKind.Prescription && product.RequiresPrescription && product.Color != PrescriptionColor.White && product.Color != Color)
            throw new DomainException($"{product.Name} {ContributionRules.ColorName(product.Color).ToLowerInvariant()} reçeteye tabidir.");
        lot.Take(quantity, today);
        var line = new SaleLine(this, product, lot, quantity);
        _lines.Add(line);
        return line;
    }

    /// <summary>Satışı kapatır: payları hesaplar, maliyeti toplar.</summary>
    public void Complete(PaymentType payment, decimal discount = 0m)
    {
        if (Status != SaleStatus.Open) throw new DomainException("Satış zaten kapatılmış.");
        if (_lines.Count == 0) throw new DomainException("Satışta ürün yok.");
        if (discount < 0) throw new DomainException("İndirim negatif olamaz.");
        if (discount > 0 && Kind == SaleKind.Prescription) throw new DomainException("Reçetede indirim yapılmaz.");
        Total = _lines.Sum(l => l.LineTotal);
        if (discount > Total * ContributionRules.MaxRetailDiscount) throw new DomainException($"İndirim tutarın %{ContributionRules.MaxRetailDiscount * 100:0}'unu geçemez.");
        Cost = _lines.Sum(l => l.LineCost);
        Discount = discount;
        if (Kind == SaleKind.Retail)
        {
            PatientShare = Total - discount;
            InstitutionShare = 0m;
        }
        else
        {
            PatientShare = ContributionRules.PatientShare(Insurance, Patient?.SgkStatus ?? SgkStatus.Worker, Patient?.ChronicReport ?? false,
                _lines.Select(l => (l.LineTotal, l.Reimbursed, l.Quantity)));
            InstitutionShare = Math.Max(0m, Total - PatientShare);
        }
        Payment = payment;
        Status = SaleStatus.Completed;
    }

    /// <summary>İade: satış iptal edilir, kutular partilerine geri döner.</summary>
    public void Return(DateTime at)
    {
        if (Status != SaleStatus.Completed) throw new DomainException("Yalnızca tamamlanmış satış iade edilebilir.");
        foreach (var line in _lines) line.Lot.Put(line.Quantity);
        Status = SaleStatus.Returned;
        ReturnedAt = at;
    }

    /// <summary>Örnek veri: geçmiş satışın tarihini ayarlar.</summary>
    public void ImportAt(DateTime at) => SoldAt = at;
}

public sealed class SaleLine : Entity
{
    private SaleLine() { }

    internal SaleLine(Sale sale, Product product, StockLot lot, int quantity)
    {
        Sale = sale;
        Product = product;
        ProductId = product.Id;
        Lot = lot;
        LotId = lot.Id;
        Quantity = quantity;
        UnitPrice = product.SalePrice;
        UnitCost = lot.UnitCost;
        Reimbursed = sale.Kind == SaleKind.Prescription && product.Reimbursed && sale.Insurance == InsuranceType.Sgk;
    }

    public int SaleId { get; private set; }
    public Sale Sale { get; private set; } = null!;
    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int LotId { get; private set; }
    public StockLot Lot { get; private set; } = null!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal UnitCost { get; private set; }

    /// <summary>SGK bu kalemi ödüyor mu (geri ödeme listesinde ve hasta SGK'lı)?</summary>
    public bool Reimbursed { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;
    public decimal LineCost => Quantity * UnitCost;
}

/// <summary>
/// Katılım payı kuralları. Örnek amaçlı, sade bir modeldir; gerçek mevzuattaki tutarlar ve istisnalar dönem dönem değişir.
/// SGK: reçete başına kutu ücreti (ilk 3 kutu 3 ₺, sonraki her kutu 1 ₺) + geri ödenen ilaçlarda yüzde pay
/// (çalışan %20, emekli %10, kronik raporlu %0); geri ödeme listesinde olmayan ilacın tamamını hasta öder.
/// Özel sigorta: hasta %20 öder, kalanı sigortaya fatura edilir. Güvencesiz hasta tamamını öder.
/// </summary>
public static class ContributionRules
{
    public const decimal FirstBoxesFee = 3m, ExtraBoxFee = 1m;
    public const int FirstBoxes = 3;
    public const decimal WorkerRate = 0.20m, RetiredRate = 0.10m, PrivateRate = 0.20m;
    public const decimal MaxRetailDiscount = 0.15m;

    public static decimal PatientShare(InsuranceType insurance, SgkStatus status, bool chronic, IEnumerable<(decimal Total, bool Reimbursed, int Boxes)> lines)
    {
        var list = lines.ToList();
        var total = list.Sum(l => l.Total);
        switch (insurance)
        {
            case InsuranceType.None:
                return total;
            case InsuranceType.Private:
                return Math.Round(total * PrivateRate, 2);
        }
        var covered = list.Where(l => l.Reimbursed).ToList();
        var notCovered = total - covered.Sum(l => l.Total);
        var boxes = covered.Sum(l => l.Boxes);
        var boxFee = boxes == 0 ? 0m : FirstBoxesFee + Math.Max(0, boxes - FirstBoxes) * ExtraBoxFee;
        var rate = chronic ? 0m : status == SgkStatus.Retired ? RetiredRate : WorkerRate;
        var share = boxFee + covered.Sum(l => l.Total) * rate + notCovered;
        return Math.Min(total, Math.Round(share, 2));
    }

    /// <summary>Reçetenin karşılanabileceği süre (gün): beyaz 3 iş günü yerine sade olarak 5 gün, renkli reçeteler 2 gün.</summary>
    public static int ValidityDays(PrescriptionColor color) => color == PrescriptionColor.White ? 5 : 2;

    public static string ColorName(PrescriptionColor c) => c switch
    {
        PrescriptionColor.Red => "Kırmızı",
        PrescriptionColor.Green => "Yeşil",
        PrescriptionColor.Purple => "Mor",
        PrescriptionColor.Orange => "Turuncu",
        _ => "Beyaz",
    };
}

public enum Institution { Sgk, PrivateInsurance }

/// <summary>Kuruma (SGK ya da özel sigorta) kesilen aylık fatura: o ayın reçetelerindeki kurum payı.</summary>
public sealed class InstitutionInvoice : AuditableEntity
{
    private InstitutionInvoice() { }

    public InstitutionInvoice(Institution institution, int year, int month, decimal amount, int prescriptions, DateOnly issuedOn)
    {
        if (month is < 1 or > 12) throw new DomainException("Ay 1-12 arasında olmalı.");
        if (amount < 0) throw new DomainException("Tutar negatif olamaz.");
        Institution = institution;
        Year = year;
        Month = month;
        Amount = amount;
        Prescriptions = prescriptions;
        IssuedOn = issuedOn;
    }

    public Institution Institution { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public decimal Amount { get; private set; }
    public int Prescriptions { get; private set; }
    public DateOnly IssuedOn { get; private set; }
    public DateOnly? PaidOn { get; private set; }

    public bool IsPaid => PaidOn is not null;

    public void MarkPaid(DateOnly on)
    {
        if (IsPaid) throw new DomainException("Fatura zaten tahsil edilmiş.");
        if (on < IssuedOn) throw new DomainException("Tahsil tarihi fatura tarihinden önce olamaz.");
        PaidOn = on;
    }
}
