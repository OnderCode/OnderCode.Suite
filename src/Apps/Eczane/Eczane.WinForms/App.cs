using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;

namespace Eczane.WinForms;

/// <summary>Arayüzün uygulama katmanına tek kapısı ve ortak görsel kurallar.</summary>
static class App
{
    public static IServiceProvider Services { get; set; } = null!;

    public static Task<TResponse> Send<TResponse>(IRequest<TResponse> request) where TResponse : IResultType<TResponse>
        => Services.GetRequiredService<ISender>().SendAsync(request);

    public static DateTime Now => Services.GetRequiredService<TimeProvider>().Now();
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    public static void ShowErrors(IResult result, string title = "İşlem yapılamadı")
        => OndaToast.Error(string.Join(Environment.NewLine, result.Errors.Select(e => e.Message)), title);

    public static string Money(decimal value) => value.ToString("N2") + " ₺";

    public static readonly Color Violet = Color.FromArgb(139, 92, 246);
    public static readonly Color Cyan = Color.FromArgb(6, 182, 212);
    public static readonly Color Teal = Color.FromArgb(20, 184, 166);
    public static readonly Color Amber = Color.FromArgb(245, 158, 11);
    public static readonly Color Pink = Color.FromArgb(236, 72, 153);
    public static readonly Color Slate = Color.FromArgb(148, 163, 184);

    /// <summary>Reçete renkleri: beyaz, kırmızı, yeşil, mor, turuncu.</summary>
    public static Color RxColor(string? name) => name switch
    {
        "Kırmızı" => Color.FromArgb(220, 38, 38),
        "Yeşil" => Color.FromArgb(22, 163, 74),
        "Mor" => Color.FromArgb(124, 58, 237),
        "Turuncu" => Color.FromArgb(234, 88, 12),
        "Beyaz" => Slate,
        _ => OndaTheme.Colors.TextMuted,
    };

    public static Color BadgeColor(object? value) => (value as string) switch
    {
        "Kırmızı" or "Yeşil" or "Mor" or "Turuncu" or "Beyaz" => RxColor((string)value!),
        "Yeterli" or "Tamamlandı" or "Çalışıyor" or "Kullanımda" or "Teslim alındı" or "Tahsil edildi" or "A" or "Uygun" or "Raporlu" or "Yıllık izin" or "Periyodik bakım" => OndaTheme.Colors.Success,
        "Reçete" or "Kart" or "SGK" or "Yolda" or "Devlet hastanesi" or "İkinci eczacı" or "Reçeteli ilaç" or "Havale/EFT" or "Alım" => OndaTheme.Colors.Primary,
        "Serbest satış" or "Özel sigorta" or "Taslak" or "Kalibrasyon" or "Özel hastane" or "Mesul müdür eczacı" or "Gece" or "Dermokozmetik" or "Çek" => Violet,
        "Kritik stok" or "Nakit" or "B" or "İzlemede" or "İzinde" or "Tahsil bekliyor" or "Tamirde" or "Pazar" or "Mazeret" or "Eczane teknisyeni" or "Reçetesiz ilaç" or "Sayım farkı" => OndaTheme.Colors.Warning,
        "Stokta yok" or "İade" or "İptal" or "SKT geçti" or "Kritik" or "Ayrıldı" or "Faturalanmadı" or "Onarım" or "Rapor" or "Bayram" or "Hurda" or "İmha" or "Güvencesiz" or "Gecikmiş" => OndaTheme.Colors.Danger,
        "Şehir hastanesi" or "Üniversite hastanesi" or "Aile sağlığı merkezi" or "Eczane çalışanı" or "Gıda takviyesi" or "Satış" or "C" => Cyan,
        "Medikal" or "Anne-bebek" or "Ağız ve diş sağlığı" or "Stajyer" or "Müşteri iadesi" or "Depoya iade" => Teal,
        _ => OndaTheme.Colors.TextMuted,
    };

    /// <summary>Grafik etiketi için kısa kurum adı: "Merkez 1 No'lu Aile Sağlığı Merkezi" → "ASM 1".</summary>
    public static string ShortHospital(string name)
    {
        var w = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length > 1 && w[0] == "Merkez") return "ASM " + w[1];
        if (name.Contains("Kadın Doğum")) return w[0] + " KDÇ";
        return w[0];
    }

    public static string Percent(double v) => "%" + (v * 100).ToString("0.0");
}
