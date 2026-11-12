namespace Otopark.Domain;

/// <summary>Bir kalışın analiz için gereken özeti.</summary>
public readonly record struct StaySpan(DateTime Entry, DateTime? Exit, VehicleClass Vehicle);

/// <summary>Doluluk hesapları: saat saat içerideki araç sayısı.</summary>
public static class LotKpi
{
    /// <summary>[from, from + hours) aralığında her saatin ortasında içeride olan araç sayısı.</summary>
    public static int[] HourlyOccupancy(DateTime from, int hours, IEnumerable<StaySpan> stays, DateTime now)
    {
        var result = new int[hours];
        foreach (var s in stays)
        {
            var exit = s.Exit ?? now;
            for (var h = 0; h < hours; h++)
            {
                var mid = from.AddHours(h + 0.5);
                if (s.Entry <= mid && mid < exit) result[h]++;
            }
        }
        return result;
    }

    /// <summary>Gün içinde aynı anda içeride bulunan en yüksek araç sayısı (giriş/çıkış olaylarını tarayarak).</summary>
    public static int PeakConcurrent(DateTime from, DateTime to, IEnumerable<StaySpan> stays, DateTime now)
    {
        var events = new List<(DateTime At, int Delta)>();
        foreach (var s in stays)
        {
            var exit = s.Exit ?? now;
            if (exit <= from || s.Entry >= to) continue;
            events.Add((s.Entry < from ? from : s.Entry, +1));
            events.Add((exit > to ? to : exit, -1));
        }
        var current = 0;
        var peak = 0;
        foreach (var e in events.OrderBy(e => e.At).ThenBy(e => e.Delta))
        {
            current += e.Delta;
            peak = Math.Max(peak, current);
        }
        return peak;
    }
}
