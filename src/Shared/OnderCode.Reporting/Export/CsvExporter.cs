using System.Globalization;
using System.Text;

namespace OnderCode.Reporting.Export;

/// <summary>
/// CSV: yalnızca tablolar yazılır. Türkçe Excel'in doğrudan açabilmesi için varsayılan ayraç noktalı virgül,
/// ondalık ayracı virgül, kodlama UTF-8 (BOM'lu). Birden çok tablo varsa her biri başlık satırıyla ve boş satırla ayrılır.
/// Metin hücreleri =, +, -, @ ile başlıyorsa başına ' eklenir (CSV formül enjeksiyonuna karşı).
/// </summary>
public sealed class CsvExporter(char delimiter = ';', CultureInfo? culture = null) : IReportExporter
{
    private readonly CultureInfo _culture = culture ?? ReportFormat.Turkish;

    public string Name => "CSV";
    public string Extension => ".csv";

    public void Export(ReportDocument document, Stream output)
    {
        using var w = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true) { NewLine = "\r\n" };
        var many = document.Tables.Count > 1;
        for (var t = 0; t < document.Tables.Count; t++)
        {
            var table = document.Tables[t];
            if (t > 0) w.WriteLine();
            if (many) w.WriteLine(Field(table.Title));
            w.WriteLine(string.Join(delimiter, table.Columns.Select(c => Field(Header(c)))));
            foreach (var row in table.Rows)
                w.WriteLine(string.Join(delimiter, table.Columns.Select((c, i) => Field(Value(row[i], c), c.Kind == ColumnKind.Text))));
        }
    }

    private static string Header(ReportColumn c) => c.Kind == ColumnKind.Percent && !c.Header.Contains('%') ? c.Header + " (%)" : c.Header;

    private string Value(object? value, ReportColumn column) => value switch
    {
        null => "",
        double d when !double.IsFinite(d) => "",
        string s => s,
        DateOnly d => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString(column.Kind == ColumnKind.Date ? "dd.MM.yyyy" : "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
        _ => column.Kind switch
        {
            ColumnKind.Percent => (Convert.ToDouble(value, CultureInfo.InvariantCulture) * 100).ToString("0.####", _culture),
            ColumnKind.Money => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("0.00", _culture),
            ColumnKind.Integer => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.####", _culture),
        },
    };

    private string Field(string text, bool guardFormula = true)
    {
        if (guardFormula && text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
        var quote = text.Contains(delimiter) || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
                    || (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])));
        return quote ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}
