using OnderCode.Analytics;

namespace OnderCode.Reporting.Tests;

public class ParetoTests
{
    [Fact]
    public void Siniflar_kumulatif_paya_gore_belirlenir()
    {
        var values = new[] { ("a", 50m), ("b", 25m), ("c", 10m), ("d", 6m), ("e", 5m), ("f", 4m) };

        var result = Pareto.Analyze(values, x => x.Item2);

        Assert.Equal(100m, result.Total);
        Assert.Equal(new[] { "a", "b", "c", "d", "e", "f" }, result.Items.Select(i => i.Item.Item1));
        // a öncesi %0, b öncesi %50, c öncesi %75 → A; d öncesi %85, e öncesi %91 → B; f öncesi %96 → C
        Assert.Equal(new[] { AbcClass.A, AbcClass.A, AbcClass.A, AbcClass.B, AbcClass.B, AbcClass.C }, result.Items.Select(i => i.Class));
        Assert.Equal(1.0, result.Items[^1].Cumulative, 6);
        Assert.Equal(0.85, result.ValueShareOf(AbcClass.A), 6);
        Assert.Equal(0.5, result.CountShareOf(AbcClass.A), 6);
        Assert.Equal(1.0, result.Items.Sum(i => i.Share), 6);
    }

    [Fact]
    public void Tek_baskin_kalem_A_olur()
    {
        var result = Pareto.Analyze(new[] { 90m, 5m, 5m }, x => x);
        Assert.Equal(AbcClass.A, result.Items[0].Class);
        Assert.Equal(AbcClass.B, result.Items[1].Class);
    }

    [Fact]
    public void Sifir_ve_negatif_degerler_paya_katilmaz()
    {
        var result = Pareto.Analyze(new[] { 10m, 0m, -5m }, x => x);
        Assert.Equal(10m, result.Total);
        Assert.All(result.Items.Skip(1), i => Assert.Equal(AbcClass.C, i.Class));
        Assert.Equal(0, result.Items[2].Share);
    }

    [Fact]
    public void Bos_liste_hata_vermez()
    {
        var result = Pareto.Analyze(Array.Empty<decimal>(), x => x);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.ValueShareOf(AbcClass.A));
    }
}

public class ForecastTests
{
    [Fact]
    public void Dogrusal_trend_kusursuz_doguyu_bulur()
    {
        var y = Enumerable.Range(0, 20).Select(t => 5 + 2.0 * t).ToArray();

        var f = Forecast.Linear(y, 3);

        Assert.Equal(2.0, f.Parameters["slope"], 9);
        Assert.Equal(5.0, f.Parameters["intercept"], 9);
        Assert.Equal(new[] { 45.0, 47.0, 49.0 }, f.Forecast.Select(v => Math.Round(v, 9)));
        Assert.Equal(1.0, f.Parameters["r2"], 9);
    }

    [Fact]
    public void Holt_dogrusal_seride_trendi_surdurur()
    {
        var y = Enumerable.Range(0, 30).Select(t => 100 + 3.0 * t).ToArray();

        var f = Forecast.Holt(y, 5);

        Assert.Equal(ForecastMethod.Holt, f.Method);
        for (var h = 0; h < 5; h++) Assert.InRange(f.Forecast[h], 190 + 3 * h - 1.5, 190 + 3 * h + 1.5);
        Assert.True(f.Mae < 0.5);
    }

    [Fact]
    public void Holt_Winters_haftalik_deseni_tekrarlar()
    {
        double[] week = [60, 55, 58, 62, 80, 92, 75];
        var y = Enumerable.Range(0, 56).Select(t => week[t % 7] + 0.1 * t).ToArray();

        var f = Forecast.HoltWinters(y, 7, 7);

        for (var h = 0; h < 7; h++)
        {
            var expected = week[(56 + h) % 7] + 0.1 * (56 + h);
            Assert.InRange(f.Forecast[h], expected - 2, expected + 2);
        }
        Assert.Equal(7, f.Parameters["season"]);
        Assert.True(double.IsNaN(f.Fitted[0]));
    }

    [Fact]
    public void Otomatik_secim_mevsimsel_seride_Holt_Winters_secer()
    {
        double[] week = [60, 55, 58, 62, 80, 92, 75];
        var rnd = new Random(7);
        var y = Enumerable.Range(0, 63).Select(t => week[t % 7] + rnd.NextDouble() * 2 - 1).ToArray();

        var f = Forecast.Auto(y, 14, seasonLength: 7);

        Assert.Equal(ForecastMethod.HoltWinters, f.Method);
        Assert.Equal(14, f.Forecast.Length);
        Assert.True(f.Parameters.ContainsKey("holdout_mae"));
    }

    [Fact]
    public void Aralik_tahminin_iki_yanini_sarar_ve_ileriye_dogru_genisler()
    {
        var rnd = new Random(1);
        var y = Enumerable.Range(0, 40).Select(t => 50 + t * 0.5 + rnd.NextDouble() * 6).ToArray();

        var f = Forecast.Holt(y, 10);

        for (var h = 0; h < 10; h++) Assert.True(f.Lower[h] < f.Forecast[h] && f.Forecast[h] < f.Upper[h]);
        Assert.True(f.Upper[9] - f.Lower[9] > f.Upper[0] - f.Lower[0]);
        Assert.InRange(f.Coverage, 0.79, 0.81);
    }

    [Fact]
    public void Kirpma_sinirlari_uygular()
    {
        var y = Enumerable.Range(0, 20).Select(t => 0.5 + t * 0.04).ToArray();
        var f = Forecast.Linear(y, 10).Clamp(0, 1);
        Assert.All(f.Forecast, v => Assert.InRange(v, 0, 1));
        Assert.All(f.Upper, v => Assert.InRange(v, 0, 1));
    }

    [Fact]
    public void Gecersiz_seri_anlasilir_hata_verir()
    {
        Assert.Throws<ArgumentException>(() => Forecast.HoltWinters([1, 2, 3], 7, 3));
        Assert.Throws<ArgumentException>(() => Forecast.Linear([1, double.NaN, 3], 3));
    }

    [Fact]
    public void Hareketli_ortalama_ve_buyume()
    {
        var ma = Series.MovingAverage([1, 2, 3, 4, 5], 3);
        Assert.True(double.IsNaN(ma[1]));
        Assert.Equal(new[] { 2.0, 3.0, 4.0 }, ma[2..]);
        Assert.Equal(0.25, Series.Growth(125m, 100m));
        Assert.Null(Series.Growth(5m, 0m));
    }
}
