using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application;
using Otel.Domain;

namespace Otel.Application;

public static class OtelApplication
{
    public static IServiceCollection AddOtelApplication(this IServiceCollection services)
        => services.AddSuiteApplication(typeof(OtelApplication).Assembly);

    /// <summary>Otelin yerel takvim günü.</summary>
    public static DateOnly Today(this TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}

/// <summary>Durum ve türlerin ekranda görünen Türkçe adları.</summary>
public static class Texts
{
    public static string Of(RoomStatus s) => s switch
    {
        RoomStatus.Available => "Boş",
        RoomStatus.Occupied => "Dolu",
        RoomStatus.Dirty => "Kirli",
        _ => "Bakımda",
    };

    public static string Of(ReservationStatus s) => s switch
    {
        ReservationStatus.Confirmed => "Onaylı",
        ReservationStatus.CheckedIn => "Konaklıyor",
        ReservationStatus.CheckedOut => "Çıkış yaptı",
        _ => "İptal",
    };

    public static string Of(ChargeCategory c) => c switch
    {
        ChargeCategory.Minibar => "Minibar",
        ChargeCategory.Restaurant => "Restoran",
        ChargeCategory.Laundry => "Çamaşırhane",
        ChargeCategory.Spa => "Spa",
        ChargeCategory.Transfer => "Transfer",
        _ => "Diğer",
    };

    public static string Of(LoyaltyTier t) => t switch
    {
        LoyaltyTier.Gold => "Altın",
        LoyaltyTier.Silver => "Gümüş",
        _ => "Standart",
    };

    public static string Of(TaskKind k) => k == TaskKind.Cleaning ? "Temizlik" : "Bakım";

    public static string Of(TaskState s) => s == TaskState.Open ? "Açık" : "Tamamlandı";
}
