using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using Otel.Domain;

namespace Otel.WinForms;

/// <summary>Arayüzün uygulama katmanına tek kapısı ve ortak görsel kurallar.</summary>
static class App
{
    public static IServiceProvider Services { get; set; } = null!;

    public static Task<TResponse> Send<TResponse>(IRequest<TResponse> request) where TResponse : IResultType<TResponse>
        => Services.GetRequiredService<ISender>().SendAsync(request);

    public static DateOnly Today => DateOnly.FromDateTime(Services.GetRequiredService<TimeProvider>().GetLocalNow().DateTime);

    public static void ShowErrors(IResult result, string title = "İşlem yapılamadı")
        => OndaToast.Error(string.Join(Environment.NewLine, result.Errors.Select(e => e.Message)), title);

    public static string Money(decimal value) => value.ToString("N2") + " ₺";

    // Renk dili bütün ekranlarda aynı: yeşil boş, mavi dolu, mor gelecek, sarı kirli, kırmızı bakım
    public static readonly Color Violet = Color.FromArgb(139, 92, 246);

    public static Color ColorOf(RoomStatus s) => s switch
    {
        RoomStatus.Available => OndaTheme.Colors.Success,
        RoomStatus.Occupied => OndaTheme.Colors.Primary,
        RoomStatus.Dirty => OndaTheme.Colors.Warning,
        _ => OndaTheme.Colors.Danger,
    };

    public static Color ColorOf(ReservationStatus s) => s switch
    {
        ReservationStatus.Confirmed => Violet,
        ReservationStatus.CheckedIn => OndaTheme.Colors.Primary,
        ReservationStatus.CheckedOut => OndaTheme.Colors.TextMuted,
        _ => OndaTheme.Colors.Danger,
    };

    /// <summary>Grid rozetleri için metne göre renk.</summary>
    public static Color BadgeColor(object? value) => (value as string) switch
    {
        "Boş" or "Giriş" or "Tamamlandı" => OndaTheme.Colors.Success,
        "Dolu" or "Konaklıyor" => OndaTheme.Colors.Primary,
        "Onaylı" => Violet,
        "Kirli" or "Çıkış" or "Açık" or "Temizlik" => OndaTheme.Colors.Warning,
        "Bakımda" or "İptal" or "Bakım" => OndaTheme.Colors.Danger,
        "Altın" => Color.FromArgb(217, 119, 6),
        "Gümüş" => Color.FromArgb(100, 116, 139),
        _ => OndaTheme.Colors.TextMuted,
    };
}
