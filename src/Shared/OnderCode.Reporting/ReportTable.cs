using System.Globalization;

namespace OnderCode.Reporting;

public enum ColumnKind { Text, Integer, Number, Money, Percent, Date, DateTime }

/// <summary>Tablo sütunu. Yüzde değerleri 0..1 arasında tutulur (0,784 → %78,4).</summary>
public sealed class ReportColumn
{
    internal ReportColumn(string header, ColumnKind kind, double width, bool total, int decimals)
    {
        Header = header;
        Kind = kind;
        Width = width;
        Total = total;
        Decimals = decimals;
    }

    public string Header { get; }
    public ColumnKind Kind { get; }

    /// <summary>Göreli genişlik, yaklaşık karakter sayısı. Excel'de sütun genişliği, PDF'te sayfaya oranlanan pay.</summary>
    public double Width { get; }

    /// <summary>Alt satırda toplamı gösterilsin mi?</summary>
    public bool Total { get; }

    /// <summary>Ondalık hane (Number ve Percent için).</summary>
    public int Decimals { get; }

    public bool IsNumeric => Kind is ColumnKind.Integer or ColumnKind.Number or ColumnKind.Money or ColumnKind.Percent;
}

/// <summary>Rapor tablosu. Satırlar sütun sırasıyla değer dizisidir: string, long, double, decimal, DateOnly, DateTime ya da null.</summary>
public sealed class ReportTable
{
    internal ReportTable(string title, string? description, IReadOnlyList<ReportColumn> columns, IReadOnlyList<object?[]> rows)
    {
        Title = title;
        Description = description;
        Columns = columns;
        Rows = rows;
    }

    public string Title { get; }
    public string? Description { get; }
    public IReadOnlyList<ReportColumn> Columns { get; }
    public IReadOnlyList<object?[]> Rows { get; }
    public bool HasTotals => Columns.Any(c => c.Total);

    /// <summary>Toplam satırı: toplanan sütunlarda toplam, diğerlerinde null.</summary>
    public object?[] Totals()
    {
        var result = new object?[Columns.Count];
        for (var c = 0; c < Columns.Count; c++)
        {
            if (!Columns[c].Total) continue;
            result[c] = Columns[c].Kind switch
            {
                ColumnKind.Money => Rows.Sum(r => r[c] is decimal d ? d : 0m),
                ColumnKind.Integer => Rows.Sum(r => r[c] is long l ? l : 0L),
                _ => Rows.Sum(r => r[c] is double d ? (double.IsFinite(d) ? d : 0) : 0),
            };
        }
        return result;
    }

    /// <summary>Nesne listesinden tablo kurar: <c>ReportTable.For("Odalar", list).Text("Oda", x => x.No).Money("Gelir", x => x.Gelir, total: true).Build()</c></summary>
    public static ReportTableBuilder<T> For<T>(string title, IEnumerable<T> items) => new(title, items);
}

public sealed class ReportTableBuilder<T>
{
    private readonly string _title;
    private readonly IEnumerable<T> _items;
    private readonly List<(ReportColumn Column, Func<T, object?> Get)> _columns = [];
    private string? _description;

    internal ReportTableBuilder(string title, IEnumerable<T> items)
    {
        _title = title;
        _items = items;
    }

    public ReportTableBuilder<T> Description(string? text) { _description = text; return this; }

    public ReportTableBuilder<T> Text(string header, Func<T, string?> get, double width = 18)
        => Add(header, ColumnKind.Text, width, false, 0, x => get(x));

    public ReportTableBuilder<T> Integer(string header, Func<T, long> get, double width = 9, bool total = false)
        => Add(header, ColumnKind.Integer, width, total, 0, x => get(x));

    public ReportTableBuilder<T> Number(string header, Func<T, double> get, int decimals = 2, double width = 10, bool total = false)
        => Add(header, ColumnKind.Number, width, total, decimals, x => get(x));

    public ReportTableBuilder<T> Money(string header, Func<T, decimal> get, double width = 14, bool total = false)
        => Add(header, ColumnKind.Money, width, total, 2, x => get(x));

    public ReportTableBuilder<T> Percent(string header, Func<T, double> get, int decimals = 1, double width = 9, bool total = false)
        => Add(header, ColumnKind.Percent, width, total, decimals, x => get(x));

    public ReportTableBuilder<T> Date(string header, Func<T, DateOnly> get, double width = 11)
        => Add(header, ColumnKind.Date, width, false, 0, x => get(x));

    public ReportTableBuilder<T> DateTime(string header, Func<T, DateTime> get, double width = 16)
        => Add(header, ColumnKind.DateTime, width, false, 0, x => get(x));

    public ReportTable Build()
    {
        if (_columns.Count == 0) throw new InvalidOperationException("Tabloda en az bir sütun olmalı.");
        var rows = _items.Select(item => _columns.Select(c => c.Get(item)).ToArray()).ToList();
        return new ReportTable(_title, _description, [.. _columns.Select(c => c.Column)], rows);
    }

    private ReportTableBuilder<T> Add(string header, ColumnKind kind, double width, bool total, int decimals, Func<T, object?> get)
    {
        _columns.Add((new ReportColumn(header, kind, Math.Max(3, width), total, Math.Clamp(decimals, 0, 4)), get));
        return this;
    }
}

/// <summary>Türkçe değer biçimleri: 1.234,56 ₺ · %78,4 · 11.11.2026.</summary>
public static class ReportFormat
{
    public static CultureInfo Turkish { get; } = CultureInfo.GetCultureInfo("tr-TR");

    public static string Money(decimal value) => value.ToString("N2", Turkish) + " ₺";

    public static string Percent(double value, int decimals = 1)
    {
        var text = "%" + Math.Abs(value * 100).ToString("N" + decimals, Turkish);
        return value < 0 ? "-" + text : text;
    }

    public static string Integer(long value) => value.ToString("N0", Turkish);

    public static string Number(double value, int decimals = 2) => value.ToString("N" + decimals, Turkish);

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", Turkish);

    public static string DateTime(DateTime value) => value.ToString("dd.MM.yyyy HH:mm", Turkish);

    /// <summary>Kısaltılmış sayı: 1,2 Mn · 350 B · 42.</summary>
    public static string Compact(double value)
    {
        var a = Math.Abs(value);
        return a >= 1e6 ? (value / 1e6).ToString("0.#", Turkish) + " Mn"
             : a >= 1e3 ? (value / 1e3).ToString("0.#", Turkish) + " B"
             : value.ToString("0.#", Turkish);
    }

    /// <summary>Hücre değerini sütun türüne göre metne çevirir (PDF ve ekran için).</summary>
    public static string Cell(object? value, ReportColumn column) => value switch
    {
        null => "",
        string s => s,
        double d when !double.IsFinite(d) => "",
        _ => column.Kind switch
        {
            ColumnKind.Money => Money(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
            ColumnKind.Percent => Percent(Convert.ToDouble(value, CultureInfo.InvariantCulture), column.Decimals),
            ColumnKind.Integer => Integer(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            ColumnKind.Number => Number(Convert.ToDouble(value, CultureInfo.InvariantCulture), column.Decimals),
            ColumnKind.Date when value is DateOnly d => Date(d),
            ColumnKind.Date when value is DateTime dt => Date(DateOnly.FromDateTime(dt)),
            ColumnKind.DateTime when value is DateTime dt => DateTime(dt),
            _ => Convert.ToString(value, Turkish) ?? "",
        },
    };
}
