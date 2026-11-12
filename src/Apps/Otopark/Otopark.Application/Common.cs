using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application;
using Otopark.Domain;

namespace Otopark.Application;

public static class OtoparkApplication
{
    public static IServiceCollection AddOtoparkApplication(this IServiceCollection services)
        => services.AddSuiteApplication(typeof(OtoparkApplication).Assembly);

    /// <summary>Otoparkın yerel saati. Giriş ve çıkış saatleri yerel saatle saklanır; saat bazlı yoğunluk analizi böyle doğal okunur.</summary>
    public static DateTime Now(this TimeProvider time) => DateTime.SpecifyKind(time.GetLocalNow().DateTime, DateTimeKind.Unspecified);

    public static DateOnly Today(this TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    internal static DateTime Start(this DateOnly day) => day.ToDateTime(TimeOnly.MinValue);
}

/// <summary>Ekranda görünen Türkçe adlar ve küçük biçim yardımcıları.</summary>
public static class Texts
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Of(VehicleClass v) => v switch
    {
        VehicleClass.Large => "Büyük araç",
        VehicleClass.Motorcycle => "Motosiklet",
        _ => "Otomobil",
    };

    public static string Of(SpotKind k) => k switch
    {
        SpotKind.Disabled => "Engelli",
        SpotKind.Electric => "Şarj",
        SpotKind.Large => "Büyük araç",
        SpotKind.Motorcycle => "Motosiklet",
        _ => "Standart",
    };

    public static string Of(WashStatus s) => s switch
    {
        WashStatus.Waiting => "Bekliyor",
        WashStatus.Washing => "Yıkanıyor",
        WashStatus.Ready => "Hazır",
        WashStatus.Delivered => "Teslim edildi",
        _ => "İptal",
    };

    public static string Of(PaymentMethod p) => p switch
    {
        PaymentMethod.Card => "Kart",
        PaymentMethod.Subscription => "Abonelik",
        _ => "Nakit",
    };

    public static string Of(SessionStatus s) => s == SessionStatus.Active ? "İçeride" : "Çıktı";

    /// <summary>"2 sa 15 dk", "1 gün 3 sa", "12 dk".</summary>
    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalDays >= 1) return $"{(int)d.TotalDays} gün {d.Hours} sa";
        if (d.TotalHours >= 1) return $"{(int)d.TotalHours} sa {d.Minutes} dk";
        return $"{Math.Max(0, (int)d.TotalMinutes)} dk";
    }

    public static string DayName(DateOnly d) => Turkish.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek);
}
