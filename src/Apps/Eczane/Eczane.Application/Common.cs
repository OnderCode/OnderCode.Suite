using System.Globalization;
using Eczane.Domain;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application;

namespace Eczane.Application;

public static class EczaneApplication
{
    public static IServiceCollection AddEczaneApplication(this IServiceCollection services)
        => services.AddSuiteApplication(typeof(EczaneApplication).Assembly);

    /// <summary>Eczanenin yerel saati (satış ve nöbet saatleri yerel saatle saklanır).</summary>
    public static DateTime Now(this TimeProvider time) => DateTime.SpecifyKind(time.GetLocalNow().DateTime, DateTimeKind.Unspecified);

    public static DateOnly Today(this TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    internal static DateTime Start(this DateOnly day) => day.ToDateTime(TimeOnly.MinValue);
}

/// <summary>Hekim branşları (reçete girişinde seçilir, hastane/branş analizinde kullanılır).</summary>
public static class Branches
{
    public static readonly string[] All =
    [
        "Aile hekimliği", "İç hastalıkları", "Kardiyoloji", "Çocuk sağlığı", "Göğüs hastalıkları", "Endokrinoloji",
        "Ortopedi", "Dermatoloji", "Kulak burun boğaz", "Nöroloji", "Psikiyatri", "Göz hastalıkları", "Üroloji",
        "Kadın doğum", "Genel cerrahi", "Diş hekimliği", "Acil tıp", "Fizik tedavi",
    ];
}

/// <summary>Ekranda görünen Türkçe adlar ve küçük biçim yardımcıları.</summary>
public static class Texts
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Of(StaffRole r) => r switch
    {
        StaffRole.ResponsiblePharmacist => "Mesul müdür eczacı",
        StaffRole.Pharmacist => "İkinci eczacı",
        StaffRole.Technician => "Eczane teknisyeni",
        StaffRole.Assistant => "Eczane çalışanı",
        _ => "Stajyer",
    };

    public static string Of(LeaveType t) => t switch
    {
        LeaveType.Annual => "Yıllık izin",
        LeaveType.Sick => "Rapor",
        LeaveType.Excuse => "Mazeret",
        _ => "Ücretsiz izin",
    };

    public static string Of(DutyKind k) => k switch
    {
        DutyKind.Sunday => "Pazar",
        DutyKind.Holiday => "Bayram",
        _ => "Gece",
    };

    public static string Of(AssetCategory c) => c switch
    {
        AssetCategory.Furniture => "Mobilya / raf",
        AssetCategory.Computer => "Bilgisayar / donanım",
        AssetCategory.ColdChain => "Soğuk zincir",
        AssetCategory.Device => "Cihaz",
        AssetCategory.Security => "Güvenlik",
        _ => "Diğer",
    };

    public static string Of(AssetStatus s) => s switch
    {
        AssetStatus.InRepair => "Tamirde",
        AssetStatus.Retired => "Hurda",
        _ => "Kullanımda",
    };

    public static string Of(MaintenanceKind k) => k switch
    {
        MaintenanceKind.Calibration => "Kalibrasyon",
        MaintenanceKind.Repair => "Onarım",
        _ => "Periyodik bakım",
    };

    public static string Of(ProductCategory c) => c switch
    {
        ProductCategory.Rx => "Reçeteli ilaç",
        ProductCategory.Otc => "Reçetesiz ilaç",
        ProductCategory.Dermo => "Dermokozmetik",
        ProductCategory.Supplement => "Gıda takviyesi",
        ProductCategory.Medical => "Medikal",
        _ => "Anne-bebek",
    };

    public static string Of(PrescriptionColor c) => ContributionRules.ColorName(c);

    public static string Of(ExpiryLevel l) => l switch
    {
        ExpiryLevel.Expired => "SKT geçti",
        ExpiryLevel.Critical => "Kritik",
        ExpiryLevel.Watch => "İzlemede",
        _ => "Uygun",
    };

    public static string Of(MovementType t) => t switch
    {
        MovementType.Receive => "Alım",
        MovementType.Sale => "Satış",
        MovementType.Return => "Müşteri iadesi",
        MovementType.Disposal => "İmha",
        MovementType.CountAdjustment => "Sayım farkı",
        _ => "Depoya iade",
    };

    public static string Of(OrderStatus s) => s switch
    {
        OrderStatus.Draft => "Taslak",
        OrderStatus.Ordered => "Yolda",
        OrderStatus.Received => "Teslim alındı",
        _ => "İptal",
    };

    public static string Of(SupplierPaymentMethod m) => m switch
    {
        SupplierPaymentMethod.Check => "Çek",
        SupplierPaymentMethod.Card => "Kart",
        _ => "Havale/EFT",
    };

    public static string Of(Gender g) => g == Gender.Female ? "Kadın" : "Erkek";

    public static string Of(InsuranceType i) => i switch
    {
        InsuranceType.Sgk => "SGK",
        InsuranceType.Private => "Özel sigorta",
        _ => "Güvencesiz",
    };

    public static string Of(SgkStatus s) => s == SgkStatus.Retired ? "Emekli" : "Çalışan";

    public static string Of(HospitalKind k) => k switch
    {
        HospitalKind.State => "Devlet hastanesi",
        HospitalKind.City => "Şehir hastanesi",
        HospitalKind.University => "Üniversite hastanesi",
        HospitalKind.Private => "Özel hastane",
        HospitalKind.FamilyHealth => "Aile sağlığı merkezi",
        _ => "Ağız ve diş sağlığı",
    };

    public static string Of(SaleKind k) => k == SaleKind.Prescription ? "Reçete" : "Serbest satış";

    public static string Of(SaleStatus s) => s switch
    {
        SaleStatus.Returned => "İade",
        SaleStatus.Open => "Açık",
        _ => "Tamamlandı",
    };

    public static string Of(PaymentType p) => p == PaymentType.Card ? "Kart" : "Nakit";

    public static string Of(Institution i) => i == Institution.Sgk ? "SGK" : "Özel sigorta";

    public static string DayName(DateOnly d) => Turkish.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek);

    public static string MonthName(int year, int month) => new DateTime(year, month, 1).ToString("MMMM yyyy", Turkish);
}
