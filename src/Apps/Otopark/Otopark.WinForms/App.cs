using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using Otopark.Application;
using Otopark.Domain;

namespace Otopark.WinForms;

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

    // Renk dili: yeşil boş, mavi dolu, mor abone, kırmızı kullanım dışı; yıkamada sarı bekliyor, camgöbeği yıkanıyor, yeşil hazır
    public static readonly Color Violet = Color.FromArgb(139, 92, 246);
    public static readonly Color Cyan = Color.FromArgb(6, 182, 212);

    public static Color ColorOf(SpotTile t) =>
        t.OutOfService ? OndaTheme.Colors.Danger
        : !t.Occupied ? OndaTheme.Colors.Success
        : t.IsSubscriber ? Violet
        : OndaTheme.Colors.Primary;

    public static Color ColorOf(WashStatus s) => s switch
    {
        WashStatus.Waiting => OndaTheme.Colors.Warning,
        WashStatus.Washing => Cyan,
        WashStatus.Ready => OndaTheme.Colors.Success,
        WashStatus.Delivered => OndaTheme.Colors.TextMuted,
        _ => OndaTheme.Colors.Danger,
    };

    public static Color BadgeColor(object? value) => (value as string) switch
    {
        "İçeride" or "Giriş" or "Aktif" or "Hazır" or "A" => OndaTheme.Colors.Success,
        "Çıktı" or "Çıkış" or "Kart" or "Otomobil" => OndaTheme.Colors.Primary,
        "Abonelik" or "Abone" => Violet,
        "Bekliyor" or "Yakında bitiyor" or "Nakit" or "B" or "Büyük araç" => OndaTheme.Colors.Warning,
        "Yıkanıyor" or "Motosiklet" => Cyan,
        "Bitti" or "İptal" => OndaTheme.Colors.Danger,
        _ => OndaTheme.Colors.TextMuted,
    };
}
