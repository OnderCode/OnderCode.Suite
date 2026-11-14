using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application;
using Sinema.Domain;

namespace Sinema.Application;

public static class SinemaApplication
{
    public static IServiceCollection AddSinemaApplication(this IServiceCollection services)
    {
        services.AddSingleton<SeatHoldService>();
        return services.AddSuiteApplication(typeof(SinemaApplication).Assembly);
    }

    /// <summary>Sinemanın yerel saati (seans saatleri yerel saatle saklanır).</summary>
    public static DateTime Now(this TimeProvider time) => DateTime.SpecifyKind(time.GetLocalNow().DateTime, DateTimeKind.Unspecified);

    public static DateOnly Today(this TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    internal static DateTime Start(this DateOnly day) => day.ToDateTime(TimeOnly.MinValue);
}

/// <summary>Afiş dosyalarının saklandığı yer. Altyapı uygular (disk).</summary>
public interface IPosterStore
{
    /// <summary>Dosyayı kaydeder, web'den erişilecek yolu döndürür.</summary>
    Task<string> SaveAsync(int filmId, byte[] content, string extension, CancellationToken cancellationToken);
}

/// <summary>Örnek veriyi silip yeniden yükler (demo).</summary>
public interface IDemoReset
{
    Task ResetAsync(CancellationToken cancellationToken);
}

/// <summary>Ekranda görünen Türkçe adlar.</summary>
public static class Texts
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Of(AgeRating r) => r switch
    {
        AgeRating.General => "Genel izleyici",
        AgeRating.SevenPlus => "7+",
        AgeRating.ThirteenPlus => "13+",
        AgeRating.SixteenPlus => "16+",
        _ => "18+",
    };

    public static string Short(AgeRating r) => r == AgeRating.General ? "G" : Of(r);

    public static string Of(FilmStatus s) => s switch
    {
        FilmStatus.ComingSoon => "Yakında",
        FilmStatus.NowShowing => "Vizyonda",
        _ => "Arşiv",
    };

    public static string Of(SessionFormat f) => f == SessionFormat.ThreeD ? "3D" : "2D";

    public static string Of(SessionLanguage l) => l == SessionLanguage.Dubbed ? "Türkçe dublaj" : "Altyazılı";

    public static string Short(SessionLanguage l) => l == SessionLanguage.Dubbed ? "Dublaj" : "Altyazı";

    public static string Of(SeatType t) => t switch
    {
        SeatType.Vip => "VIP",
        SeatType.Accessible => "Engelli erişimli",
        SeatType.Couple => "Çift koltuk",
        _ => "Standart",
    };

    public static string Of(StaffRole r) => r switch
    {
        StaffRole.Manager => "Yönetici",
        StaffRole.BoxOffice => "Gişe ve büfe",
        _ => "Kapı görevlisi",
    };

    public static string Of(SalesChannel c) => c == SalesChannel.Online ? "Online" : "Gişe";

    public static string Of(PaymentMethod p) => p switch
    {
        PaymentMethod.Cash => "Nakit",
        PaymentMethod.Card => "Kart",
        _ => "Online (demo)",
    };

    public static string Of(OrderStatus s) => s == OrderStatus.Refunded ? "İade" : "Tamamlandı";

    public static string Of(TicketStatus s) => s switch
    {
        TicketStatus.Used => "Kullanıldı",
        TicketStatus.Refunded => "İade",
        _ => "Geçerli",
    };

    public static string Of(ConcessionCategory c) => c switch
    {
        ConcessionCategory.Popcorn => "Mısır",
        ConcessionCategory.Drink => "İçecek",
        ConcessionCategory.Snack => "Atıştırmalık",
        _ => "Menü",
    };

    public static string DayName(DateOnly d) => Turkish.DateTimeFormat.GetDayName(d.DayOfWeek);

    public static string DayShort(DateOnly d) => Turkish.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek);

    public static string Of(DayOfWeek d) => Turkish.DateTimeFormat.GetDayName(d);

    public static string Money(decimal v) => v.ToString("N2", Turkish) + " ₺";

    public static string MoneyShort(decimal v) => v.ToString("N0", Turkish) + " ₺";
}
