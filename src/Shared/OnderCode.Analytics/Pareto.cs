namespace OnderCode.Analytics;

/// <summary>ABC sınıfı: A az sayıda ama toplamın büyük kısmını yapan kalemler, C uzun kuyruk.</summary>
public enum AbcClass { A, B, C }

/// <summary>Pareto analizinde bir kalem. Share ve Cumulative 0..1 arasıdır.</summary>
public sealed record ParetoItem<T>(T Item, decimal Value, int Rank, double Share, double Cumulative, AbcClass Class);

public sealed class ParetoResult<T>
{
    internal ParetoResult(IReadOnlyList<ParetoItem<T>> items, decimal total, double aCut, double bCut)
    {
        Items = items;
        Total = total;
        ACut = aCut;
        BCut = bCut;
    }

    /// <summary>Değere göre büyükten küçüğe sıralı kalemler.</summary>
    public IReadOnlyList<ParetoItem<T>> Items { get; }
    public decimal Total { get; }
    public double ACut { get; }
    public double BCut { get; }

    public int CountOf(AbcClass c) => Items.Count(i => i.Class == c);
    public decimal ValueOf(AbcClass c) => Items.Where(i => i.Class == c).Sum(i => i.Value);

    /// <summary>Sınıfın toplam değerdeki payı (0..1).</summary>
    public double ValueShareOf(AbcClass c) => Total <= 0 ? 0 : (double)(ValueOf(c) / Total);

    /// <summary>Sınıfın kalem sayısındaki payı (0..1). "Misafirlerin %20'si gelirin %80'i" cümlesinin ilk sayısı.</summary>
    public double CountShareOf(AbcClass c) => Items.Count == 0 ? 0 : (double)CountOf(c) / Items.Count;
}

/// <summary>
/// Pareto / ABC analizi. Kalemler değere göre sıralanır, kümülatif pay hesaplanır.
/// Bir kalem, kendisinden önceki kümülatif pay A sınırının altındaysa A, B sınırının altındaysa B, değilse C olur.
/// Böylece sınırı aşan kalem de üst sınıfta kalır ve tek başına %85 yapan bir kalem A sayılır.
/// </summary>
public static class Pareto
{
    public static ParetoResult<T> Analyze<T>(IEnumerable<T> source, Func<T, decimal> value, double aCut = 0.80, double bCut = 0.95)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(value);
        if (aCut is <= 0 or >= 1 || bCut <= aCut || bCut > 1)
            throw new ArgumentOutOfRangeException(nameof(aCut), "Sınırlar 0 < A < B ≤ 1 olmalı.");

        // Negatif değer (iade gibi) paya katılmaz ama listede kalır
        var ordered = source.Select(x => (Item: x, Value: value(x))).OrderByDescending(x => x.Value).ToList();
        var total = ordered.Where(x => x.Value > 0).Sum(x => x.Value);

        var items = new List<ParetoItem<T>>(ordered.Count);
        var running = 0m;
        for (var i = 0; i < ordered.Count; i++)
        {
            var (item, v) = ordered[i];
            var before = total <= 0 ? 1 : (double)(running / total);
            if (v > 0) running += v;
            var share = total <= 0 || v <= 0 ? 0 : (double)(v / total);
            var cumulative = total <= 0 ? 0 : (double)(running / total);
            var cls = total <= 0 || v <= 0 ? AbcClass.C : before < aCut ? AbcClass.A : before < bCut ? AbcClass.B : AbcClass.C;
            items.Add(new ParetoItem<T>(item, v, i + 1, share, cumulative, cls));
        }
        return new ParetoResult<T>(items, total, aCut, bCut);
    }
}
