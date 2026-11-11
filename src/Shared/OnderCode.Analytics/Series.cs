namespace OnderCode.Analytics;

/// <summary>Zaman serisi yardımcıları.</summary>
public static class Series
{
    /// <summary>Hareketli ortalama. İlk (window - 1) değer NaN'dır.</summary>
    public static double[] MovingAverage(IReadOnlyList<double> values, int window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
        var result = new double[values.Count];
        var sum = 0d;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= window) sum -= values[i - window];
            result[i] = i >= window - 1 ? sum / window : double.NaN;
        }
        return result;
    }

    /// <summary>Önceki döneme göre değişim oranı (0,12 = %12 artış). Önceki dönem sıfırsa null.</summary>
    public static double? Growth(double current, double previous)
        => previous == 0 ? null : (current - previous) / Math.Abs(previous);

    public static double? Growth(decimal current, decimal previous)
        => previous == 0 ? null : (double)((current - previous) / Math.Abs(previous));
}
