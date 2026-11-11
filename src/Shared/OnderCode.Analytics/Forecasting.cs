namespace OnderCode.Analytics;

public enum ForecastMethod { Naive, Linear, Holt, HoltWinters }

/// <summary>
/// Tahmin sonucu. <see cref="Fitted"/> geçmiş uzunluğundadır ve modelin her güne bir gün önceden verdiği tahmini tutar
/// (üretilemeyen başlangıç değerleri NaN). <see cref="Lower"/> / <see cref="Upper"/> yaklaşık tahmin aralığıdır.
/// </summary>
public sealed record ForecastResult(
    ForecastMethod Method,
    double[] Fitted,
    double[] Forecast,
    double[] Lower,
    double[] Upper,
    double Mae,
    double Rmse,
    double? Mape,
    double Coverage,
    IReadOnlyDictionary<string, double> Parameters)
{
    public string MethodName => Method switch
    {
        ForecastMethod.Naive => "Naif (son değer)",
        ForecastMethod.Linear => "Doğrusal trend",
        ForecastMethod.Holt => "Holt (sönümlü trend)",
        _ => "Holt-Winters (trend ve mevsimsellik)",
    };

    /// <summary>Tahmin ve aralığı fiziksel sınırlara kırpar (ör. doluluk 0..1).</summary>
    public ForecastResult Clamp(double min, double max) => this with
    {
        Forecast = [.. Forecast.Select(v => Math.Clamp(v, min, max))],
        Lower = [.. Lower.Select(v => Math.Clamp(v, min, max))],
        Upper = [.. Upper.Select(v => Math.Clamp(v, min, max))],
    };
}

/// <summary>
/// Küçük ve açıklanabilir zaman serisi tahminleme.
/// Parametre verilmezse ızgara aramasıyla bir adım sonrası hata karelerini en aza indiren değerler seçilir.
/// <see cref="Auto"/> son dönemi ayırıp yöntemleri o dönem üzerinde yarıştırır, kazananı bütün seriyle yeniden kurar.
/// </summary>
public static class Forecast
{
    /// <summary>Normal dağılımda %80 aralık katsayısı.</summary>
    public const double Z80 = 1.2815515655446004;

    static readonly double[] AlphaGrid = Steps(0.05, 0.95, 0.05);
    static readonly double[] AlphaGridCoarse = Steps(0.05, 0.95, 0.10);
    static readonly double[] BetaGrid = [0.01, 0.03, 0.05, 0.1, 0.15, 0.2, 0.3, 0.5];
    static readonly double[] BetaGridCoarse = [0.01, 0.05, 0.1, 0.2];
    static readonly double[] PhiGrid = [0.80, 0.85, 0.90, 0.95, 0.98, 1.0];
    static readonly double[] PhiGridCoarse = [0.85, 0.90, 0.95, 0.98, 1.0];

    /// <summary>Her gelecek dönem için son gözlem.</summary>
    public static ForecastResult Naive(IReadOnlyList<double> y, int horizon, double z = Z80)
    {
        Validate(y, horizon, 1);
        var n = y.Count;
        var fitted = new double[n];
        fitted[0] = double.NaN;
        for (var t = 1; t < n; t++) fitted[t] = y[t - 1];
        var (mae, rmse, mape) = Errors(y, fitted);
        var last = y[^1];
        var forecast = Enumerable.Repeat(last, horizon).ToArray();
        var width = Enumerable.Range(1, horizon).Select(h => z * rmse * Math.Sqrt(h)).ToArray();
        return Build(ForecastMethod.Naive, fitted, forecast, width, mae, rmse, mape, z, new());
    }

    /// <summary>En küçük kareler doğrusu: y = a + b·t.</summary>
    public static ForecastResult Linear(IReadOnlyList<double> y, int horizon, double z = Z80)
    {
        Validate(y, horizon, 2);
        var n = y.Count;
        var xm = (n - 1) / 2.0;
        var ym = y.Average();
        double sxx = 0, sxy = 0;
        for (var t = 0; t < n; t++)
        {
            sxx += (t - xm) * (t - xm);
            sxy += (t - xm) * (y[t] - ym);
        }
        var slope = sxy / sxx;
        var intercept = ym - slope * xm;

        var fitted = new double[n];
        double sse = 0, sst = 0;
        for (var t = 0; t < n; t++)
        {
            fitted[t] = intercept + slope * t;
            sse += (y[t] - fitted[t]) * (y[t] - fitted[t]);
            sst += (y[t] - ym) * (y[t] - ym);
        }
        var (mae, rmse, mape) = Errors(y, fitted);
        var s = n > 2 ? Math.Sqrt(sse / (n - 2)) : 0;
        var forecast = new double[horizon];
        var width = new double[horizon];
        for (var h = 0; h < horizon; h++)
        {
            var x0 = n + h;
            forecast[h] = intercept + slope * x0;
            width[h] = z * s * Math.Sqrt(1 + 1.0 / n + (x0 - xm) * (x0 - xm) / sxx);
        }
        var r2 = sst == 0 ? 1 : 1 - sse / sst;
        return Build(ForecastMethod.Linear, fitted, forecast, width, mae, rmse, mape, z,
            new() { ["intercept"] = intercept, ["slope"] = slope, ["r2"] = r2 });
    }

    /// <summary>Holt'un sönümlü trendli üstel düzleştirmesi (mevsimsellik yok).</summary>
    public static ForecastResult Holt(IReadOnlyList<double> y, int horizon, double? alpha = null, double? beta = null, double? phi = null, double z = Z80)
    {
        Validate(y, horizon, 3);
        var (a, b, p) = Best(
            alpha is { } fa ? [fa] : AlphaGrid,
            beta is { } fb ? [fb] : BetaGrid,
            [0],
            phi is { } fp ? [fp] : PhiGrid,
            (a, b, _, p) => Run(y, 0, a, b, 0, p, 0).Sse);
        return Finish(ForecastMethod.Holt, y, 0, a, b, 0, p, horizon, z);
    }

    /// <summary>Toplamsal Holt-Winters: düzey + sönümlü trend + mevsim (ör. günlük veride seasonLength = 7).</summary>
    public static ForecastResult HoltWinters(IReadOnlyList<double> y, int seasonLength, int horizon,
        double? alpha = null, double? beta = null, double? gamma = null, double? phi = null, double z = Z80)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonLength, 2);
        Validate(y, horizon, seasonLength * 2 + 1);
        var (a, b, g, p) = Best4(
            alpha is { } fa ? [fa] : AlphaGridCoarse,
            beta is { } fb ? [fb] : BetaGridCoarse,
            gamma is { } fg ? [fg] : AlphaGridCoarse,
            phi is { } fp ? [fp] : PhiGridCoarse,
            (a, b, g, p) => Run(y, seasonLength, a, b, g, p, 0).Sse);
        return Finish(ForecastMethod.HoltWinters, y, seasonLength, a, b, g, p, horizon, z);
    }

    /// <summary>
    /// Yöntem seçimi: serinin sonundan bir parça ayrılır, her yöntem kalan kısımla kurulup o parçayı tahmin eder.
    /// Ayrılan parçada ortalama mutlak hatası en düşük yöntem bütün seriyle yeniden kurulur. Eşitlikte basit yöntem kazanır.
    /// </summary>
    public static ForecastResult Auto(IReadOnlyList<double> y, int horizon, int seasonLength = 0, double z = Z80)
    {
        Validate(y, horizon, 1);
        var n = y.Count;
        if (n < 6) return n >= 2 ? Linear(y, horizon, z) : Naive(y, horizon, z);

        var k = Math.Clamp(horizon, 1, n / 4);
        if (seasonLength >= 2) k = Math.Max(k, Math.Min(seasonLength, n / 4));
        var train = y.Take(n - k).ToArray();
        var test = y.Skip(n - k).ToArray();

        var candidates = new List<(ForecastMethod Method, Func<IReadOnlyList<double>, int, ForecastResult> Fit)>
        {
            (ForecastMethod.Naive, (s, h) => Naive(s, h, z)),
            (ForecastMethod.Linear, (s, h) => Linear(s, h, z)),
        };
        if (train.Length >= 4) candidates.Add((ForecastMethod.Holt, (s, h) => Holt(s, h, z: z)));
        if (seasonLength >= 2 && train.Length >= seasonLength * 2 + 2)
            candidates.Add((ForecastMethod.HoltWinters, (s, h) => HoltWinters(s, seasonLength, h, z: z)));

        var bestIndex = 0;
        var bestScore = double.PositiveInfinity;
        for (var i = 0; i < candidates.Count; i++)
        {
            var f = candidates[i].Fit(train, k).Forecast;
            var score = test.Select((v, j) => Math.Abs(v - f[j])).Average();
            if (score < bestScore - 1e-12) { bestScore = score; bestIndex = i; }
        }

        var result = candidates[bestIndex].Fit(y, horizon);
        var parameters = new Dictionary<string, double>(result.Parameters) { ["holdout_mae"] = bestScore, ["holdout"] = k };
        return result with { Parameters = parameters };
    }

    // ---- çekirdek ----

    readonly record struct RunResult(double Sse, int Count, double[] Fitted, double Level, double Trend, double[] Season);

    /// <summary>Düzleştirmeyi bir kez çalıştırır. m = 0 ise mevsimsiz Holt.</summary>
    static RunResult Run(IReadOnlyList<double> y, int m, double alpha, double beta, double gamma, double phi, int keepFitted)
    {
        var n = y.Count;
        var fitted = keepFitted > 0 ? new double[n] : Array.Empty<double>();
        double level, trend;
        var season = m > 0 ? new double[n] : Array.Empty<double>();
        int start;
        if (m > 0)
        {
            var first = Mean(y, 0, m);
            var second = Mean(y, m, m);
            // İlk iki sezonun ortalamasından trend; düzey ilk sezonun sonuna taşınır, mevsim etkisi trendden arındırılır
            trend = (second - first) / m;
            level = first + (m - 1) / 2.0 * trend;
            for (var i = 0; i < m; i++) season[i] = y[i] - (first + (i - (m - 1) / 2.0) * trend);
            start = m;
        }
        else
        {
            level = y[0];
            var d = Math.Min(4, n - 1);
            trend = (y[d] - y[0]) / d;
            start = 1;
        }
        if (keepFitted > 0) for (var t = 0; t < start; t++) fitted[t] = double.NaN;

        double sse = 0;
        var count = 0;
        for (var t = start; t < n; t++)
        {
            var s = m > 0 ? season[t - m] : 0;
            var f = level + phi * trend + s;
            if (keepFitted > 0) fitted[t] = f;
            var e = y[t] - f;
            sse += e * e;
            count++;
            var prev = level;
            level = alpha * (y[t] - s) + (1 - alpha) * (prev + phi * trend);
            trend = beta * (level - prev) + (1 - beta) * phi * trend;
            if (m > 0) season[t] = gamma * (y[t] - level) + (1 - gamma) * s;
        }
        var lastSeason = m > 0 ? season[(n - m)..n] : Array.Empty<double>();
        return new RunResult(sse, count, fitted, level, trend, lastSeason);
    }

    static ForecastResult Finish(ForecastMethod method, IReadOnlyList<double> y, int m, double alpha, double beta, double gamma, double phi, int horizon, double z)
    {
        var run = Run(y, m, alpha, beta, gamma, phi, 1);
        var (mae, rmse, mape) = Errors(y, run.Fitted);
        var sigma = run.Count == 0 ? 0 : Math.Sqrt(run.Sse / run.Count);

        // ETS(A,Ad,A) varyans katsayıları: c_j = α(1 + β·φ_j) + γ(1-α)·[j mod m = 0]
        var forecast = new double[horizon];
        var width = new double[horizon];
        double damp = 0, cumVar = 1;
        for (var h = 1; h <= horizon; h++)
        {
            damp += Math.Pow(phi, h);
            forecast[h - 1] = run.Level + damp * run.Trend + (m > 0 ? run.Season[(h - 1) % m] : 0);
            width[h - 1] = z * sigma * Math.Sqrt(cumVar);
            var phiJ = Enumerable.Range(1, h).Sum(i => Math.Pow(phi, i));
            var c = alpha * (1 + beta * phiJ) + (m > 0 && h % m == 0 ? gamma * (1 - alpha) : 0);
            cumVar += c * c;
        }

        var parameters = new Dictionary<string, double> { ["alpha"] = alpha, ["beta"] = beta, ["phi"] = phi };
        if (m > 0) { parameters["gamma"] = gamma; parameters["season"] = m; }
        return Build(method, run.Fitted, forecast, width, mae, rmse, mape, z, parameters);
    }

    static (double, double, double) Best(double[] alphas, double[] betas, double[] gammas, double[] phis, Func<double, double, double, double, double> sse)
    {
        var (a, b, _, p) = Best4(alphas, betas, gammas, phis, sse);
        return (a, b, p);
    }

    static (double, double, double, double) Best4(double[] alphas, double[] betas, double[] gammas, double[] phis, Func<double, double, double, double, double> sse)
    {
        var best = (alphas[0], betas[0], gammas[0], phis[0]);
        var bestSse = double.PositiveInfinity;
        foreach (var a in alphas)
            foreach (var b in betas)
                foreach (var g in gammas)
                    foreach (var p in phis)
                    {
                        var v = sse(a, b, g, p);
                        if (v < bestSse - 1e-12) { bestSse = v; best = (a, b, g, p); }
                    }
        return best;
    }

    static ForecastResult Build(ForecastMethod method, double[] fitted, double[] forecast, double[] width,
        double mae, double rmse, double? mape, double z, Dictionary<string, double> parameters)
    {
        var lower = forecast.Select((f, i) => f - width[i]).ToArray();
        var upper = forecast.Select((f, i) => f + width[i]).ToArray();
        var coverage = 2 * NormalCdf(z) - 1;
        return new ForecastResult(method, fitted, forecast, lower, upper, mae, rmse, mape, coverage, parameters);
    }

    static (double Mae, double Rmse, double? Mape) Errors(IReadOnlyList<double> y, double[] fitted)
    {
        double abs = 0, sq = 0, pct = 0;
        int n = 0, np = 0;
        for (var t = 0; t < y.Count; t++)
        {
            if (double.IsNaN(fitted[t])) continue;
            var e = y[t] - fitted[t];
            abs += Math.Abs(e);
            sq += e * e;
            n++;
            if (y[t] != 0) { pct += Math.Abs(e / y[t]); np++; }
        }
        if (n == 0) return (0, 0, null);
        double? mape = np == 0 ? null : pct / np;
        return (abs / n, Math.Sqrt(sq / n), mape);
    }

    static void Validate(IReadOnlyList<double> y, int horizon, int minCount)
    {
        ArgumentNullException.ThrowIfNull(y);
        ArgumentOutOfRangeException.ThrowIfLessThan(horizon, 1);
        if (y.Count < minCount) throw new ArgumentException($"Bu yöntem için en az {minCount} gözlem gerekli; {y.Count} var.", nameof(y));
        if (y.Any(v => !double.IsFinite(v))) throw new ArgumentException("Seride boş (NaN) ya da sonsuz değer var.", nameof(y));
    }

    static double Mean(IReadOnlyList<double> y, int start, int count)
    {
        double s = 0;
        for (var i = start; i < start + count; i++) s += y[i];
        return s / count;
    }

    static double[] Steps(double from, double to, double step)
        => [.. Enumerable.Range(0, (int)Math.Round((to - from) / step) + 1).Select(i => Math.Round(from + i * step, 4))];

    /// <summary>Standart normal dağılım fonksiyonu (Abramowitz-Stegun 7.1.26, hata &lt; 1.5e-7).</summary>
    static double NormalCdf(double x)
    {
        var t = 1 / (1 + 0.3275911 * Math.Abs(x) / Math.Sqrt(2));
        var erf = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x / 2);
        return x >= 0 ? (1 + erf) / 2 : (1 - erf) / 2;
    }
}
