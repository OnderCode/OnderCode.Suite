namespace OnderCode.Reporting;

/// <summary>
/// Biçimden bağımsız rapor: başlık, göstergeler (KPI), grafikler, tablolar ve notlar.
/// Aynı belge Excel'e, PDF'e ve CSV'ye yazılabilir.
/// </summary>
public sealed class ReportDocument
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }

    /// <summary>Raporu hazırlayan kurum (ör. otel adı). Başlıkta ve sayfa altında görünür.</summary>
    public string? Organization { get; init; }

    public DateTime GeneratedAt { get; init; } = DateTime.Now;

    /// <summary>PDF yatay sayfa.</summary>
    public bool Landscape { get; init; }

    public List<ReportKpi> Kpis { get; } = [];
    public List<ReportChart> Charts { get; } = [];
    public List<ReportTable> Tables { get; } = [];
    public List<string> Notes { get; } = [];
}

/// <summary>Gösterge kutusu. Değer önceden biçimlenmiş metindir (ör. "%78,4", "3.120,00 ₺").</summary>
public sealed record ReportKpi(string Label, string Value, string? Note = null);

public enum ChartKind { Line, Bar }

/// <summary>Seri. NaN değerler boşluk olarak çizilir. Renk "#RRGGBB" biçiminde; boşsa paletten gelir.</summary>
public sealed record ReportSeries(string Name, IReadOnlyList<double> Values, bool Dashed = false, string? Color = null);

public sealed class ReportChart
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public ChartKind Kind { get; init; } = ChartKind.Line;
    public required IReadOnlyList<string> Labels { get; init; }
    public List<ReportSeries> Series { get; } = [];

    /// <summary>Eksen değeri biçimi. Boşsa kısaltılmış sayı (1,2 Mn / 350 B).</summary>
    public Func<double, string>? ValueFormat { get; init; }

    /// <summary>Sabit eksen aralığı (ör. doluluk için 0..100). Boşsa veriden hesaplanır.</summary>
    public double? Min { get; init; }
    public double? Max { get; init; }

    /// <summary>Dikey işaret çizgisi (ör. "Bugün") ve etiketi.</summary>
    public int? MarkerIndex { get; init; }
    public string? MarkerLabel { get; init; }
}
