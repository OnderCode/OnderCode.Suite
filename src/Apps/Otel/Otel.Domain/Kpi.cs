namespace Otel.Domain;

/// <summary>KPI hesabı için konaklamanın sade hâli.</summary>
public readonly record struct StaySlice(DateOnly CheckIn, DateOnly CheckOut, decimal RoomTotal, string RoomType = "")
{
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

    /// <summary>[from, to) aralığına düşen gece sayısı.</summary>
    public int NightsWithin(DateOnly from, DateOnly to)
    {
        var start = CheckIn > from ? CheckIn : from;
        var end = CheckOut < to ? CheckOut : to;
        return Math.Max(0, end.DayNumber - start.DayNumber);
    }
}

/// <summary>Otelcilik göstergeleri. Occupancy ve oranlar 0..1 değil, yüzde değil: 0..1 arasıdır.</summary>
public sealed record KpiSet(decimal Occupancy, decimal Adr, decimal RevPar, decimal RoomRevenue, int SoldNights, int AvailableNights);

/// <summary>
/// OCC (doluluk) = satılan oda-gece / satılabilir oda-gece;
/// ADR (ortalama günlük fiyat) = oda geliri / satılan oda-gece;
/// RevPAR (oda başına gelir) = oda geliri / satılabilir oda-gece = OCC × ADR.
/// Bir konaklamanın geliri gecelere eşit dağıtılır.
/// </summary>
public static class HotelKpi
{
    public static KpiSet Calculate(int roomCount, DateOnly from, DateOnly to, IEnumerable<StaySlice> stays)
    {
        var available = Math.Max(0, roomCount * (to.DayNumber - from.DayNumber));
        var sold = 0;
        var revenue = 0m;
        foreach (var s in stays)
        {
            var n = s.NightsWithin(from, to);
            if (n == 0 || s.Nights == 0) continue;
            sold += n;
            revenue += s.RoomTotal / s.Nights * n;
        }
        revenue = decimal.Round(revenue, 2);
        return new KpiSet(
            Occupancy: available == 0 ? 0 : decimal.Round((decimal)sold / available, 4),
            Adr: sold == 0 ? 0 : decimal.Round(revenue / sold, 2),
            RevPar: available == 0 ? 0 : decimal.Round(revenue / available, 2),
            RoomRevenue: revenue,
            SoldNights: sold,
            AvailableNights: available);
    }

    /// <summary>Her gün için doluluk (0..1).</summary>
    public static double[] DailyOccupancy(int roomCount, DateOnly from, int days, IReadOnlyCollection<StaySlice> stays)
    {
        var result = new double[days];
        if (roomCount == 0) return result;
        for (var i = 0; i < days; i++)
        {
            var d = from.AddDays(i);
            result[i] = stays.Count(s => s.CheckIn <= d && d < s.CheckOut) / (double)roomCount;
        }
        return result;
    }
}
