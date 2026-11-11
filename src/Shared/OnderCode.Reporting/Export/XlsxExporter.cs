using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace OnderCode.Reporting.Export;

/// <summary>
/// Excel (.xlsx) yazıcısı. Harici paket kullanmaz; Office Open XML parçalarını doğrudan zip'e yazar.
/// Sayfalar: "Özet" (göstergeler ve notlar), her tablo için bir sayfa, her grafik için veri sayfası.
/// Tablolarda başlık satırı dondurulur, süzgeç açılır, sayılar gerçek sayı olarak yazılır (Excel'de toplanabilir),
/// toplam satırı süzgece duyarlı formüldür: =SUBTOTAL(109; ...).
/// </summary>
public sealed class XlsxExporter : IReportExporter
{
    public string Name => "Excel";
    public string Extension => ".xlsx";

    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const int HeaderRow = 4;

    public void Export(ReportDocument document, Stream output)
    {
        var sheets = new List<(string Name, string Xml, string? FilterRange)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (document.Kpis.Count > 0 || document.Notes.Count > 0 || document.Tables.Count == 0)
            sheets.Add((UniqueName("Özet", names), SummarySheet(document, first: true), null));
        foreach (var table in document.Tables)
        {
            var (xml, range) = TableSheet(document, table, first: sheets.Count == 0);
            sheets.Add((UniqueName(table.Title, names), xml, range));
        }
        foreach (var chart in document.Charts)
        {
            var (xml, range) = TableSheet(document, ChartAsTable(chart), first: false);
            sheets.Add((UniqueName("Grafik - " + chart.Title, names), xml, range));
        }

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Write(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
        Write(zip, "_rels/.rels", RootRels);
        Write(zip, "docProps/core.xml", Core(document));
        Write(zip, "docProps/app.xml", App(sheets.Select(s => s.Name).ToList()));
        Write(zip, "xl/workbook.xml", Workbook(sheets));
        Write(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));
        Write(zip, "xl/styles.xml", Styles.Xml);
        for (var i = 0; i < sheets.Count; i++) Write(zip, $"xl/worksheets/sheet{i + 1}.xml", sheets[i].Xml);
    }

    // ---- sayfalar ----

    private static string SummarySheet(ReportDocument doc, bool first)
    {
        var rows = new SheetBuilder();
        TitleRows(rows, doc, doc.Title, doc.Subtitle);
        var r = HeaderRow;
        if (doc.Kpis.Count > 0)
        {
            rows.Text(r, 0, "Gösterge", Styles.Header);
            rows.Text(r, 1, "Değer", Styles.HeaderRight);
            rows.Text(r, 2, "Açıklama", Styles.Header);
            foreach (var k in doc.Kpis)
            {
                r++;
                rows.Text(r, 0, k.Label, Styles.Text);
                rows.Text(r, 1, k.Value, Styles.TextRight);
                if (k.Note is not null) rows.Text(r, 2, k.Note, Styles.Muted);
            }
            r += 2;
        }
        if (doc.Notes.Count > 0)
        {
            rows.Text(r, 0, "Notlar", Styles.Bold);
            foreach (var n in doc.Notes) rows.Text(++r, 0, "• " + n, Styles.Text);
        }
        var widths = new[] { 34d, 18, 60 };
        return Sheet(rows, widths, first, freezeRows: 0, filter: null, landscape: false);
    }

    private static (string Xml, string? Filter) TableSheet(ReportDocument doc, ReportTable table, bool first)
    {
        var rows = new SheetBuilder();
        TitleRows(rows, doc, table.Title, table.Description ?? doc.Subtitle);

        for (var c = 0; c < table.Columns.Count; c++)
            rows.Text(HeaderRow, c, table.Columns[c].Header, table.Columns[c].IsNumeric ? Styles.HeaderRight : Styles.Header);

        var r = HeaderRow;
        foreach (var row in table.Rows)
        {
            r++;
            for (var c = 0; c < table.Columns.Count; c++) rows.Value(r, c, row[c], table.Columns[c], total: false);
        }
        var lastData = r;

        if (table.HasTotals && table.Rows.Count > 0)
        {
            r++;
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var col = table.Columns[c];
                if (col.Total)
                    rows.Formula(r, c, $"SUBTOTAL(109,{Ref(HeaderRow + 1, c)}:{Ref(lastData, c)})", Styles.For(col, total: true));
                else
                    rows.Text(r, c, c == 0 ? "Toplam" : "", Styles.TotalLabel);
            }
        }

        var widths = table.Columns.Select(c => Math.Clamp(Math.Max(c.Width, c.Header.Length + 3), 6, 60)).ToArray();
        string? filter = table.Rows.Count > 0 ? $"{Ref(HeaderRow, 0)}:{Ref(lastData, table.Columns.Count - 1)}" : null;
        var landscape = widths.Sum() > 110;
        return (Sheet(rows, widths, first, freezeRows: HeaderRow, filter, landscape), filter);
    }

    private static void TitleRows(SheetBuilder rows, ReportDocument doc, string title, string? subtitle)
    {
        rows.Text(1, 0, title, Styles.Title);
        rows.Height(1, 24);
        var meta = string.Join("  ·  ", new[] { subtitle, doc.Organization, "Oluşturma: " + ReportFormat.DateTime(doc.GeneratedAt) }.Where(s => !string.IsNullOrWhiteSpace(s)));
        rows.Text(2, 0, meta, Styles.Muted);
    }

    private static ReportTable ChartAsTable(ReportChart chart)
    {
        var indexes = Enumerable.Range(0, chart.Labels.Count);
        var builder = ReportTable.For(chart.Title, indexes).Description(chart.Subtitle).Text("Dönem", i => chart.Labels[i], 14);
        foreach (var s in chart.Series)
        {
            var values = s.Values;
            builder.Number(s.Name, i => i < values.Count ? values[i] : double.NaN, 2, 14);
        }
        return builder.Build();
    }

    private static string Sheet(SheetBuilder rows, IReadOnlyList<double> widths, bool first, int freezeRows, string? filter, bool landscape)
    {
        var sb = new StringBuilder();
        sb.Append($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="{MainNs}" xmlns:r="{RelNs}">""");
        sb.Append("""<sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>""");
        sb.Append($"""<dimension ref="A1:{Ref(Math.Max(1, rows.LastRow), Math.Max(0, Math.Max(rows.LastColumn, widths.Count - 1)))}"/>""");
        sb.Append($"""<sheetViews><sheetView workbookViewId="0"{(first ? " tabSelected=\"1\"" : "")} showGridLines="0">""");
        if (freezeRows > 0)
        {
            var top = Ref(freezeRows + 1, 0);
            sb.Append($"""<pane ySplit="{freezeRows}" topLeftCell="{top}" activePane="bottomLeft" state="frozen"/><selection pane="bottomLeft" activeCell="{top}" sqref="{top}"/>""");
        }
        sb.Append("</sheetView></sheetViews>");
        sb.Append("""<sheetFormatPr defaultRowHeight="15"/>""");
        sb.Append("<cols>");
        for (var i = 0; i < widths.Count; i++)
            sb.Append($"""<col min="{i + 1}" max="{i + 1}" width="{N(widths[i])}" customWidth="1"/>""");
        sb.Append("</cols>");
        sb.Append("<sheetData>");
        rows.AppendRows(sb);
        sb.Append("</sheetData>");
        if (filter is not null) sb.Append($"""<autoFilter ref="{filter}"/>""");
        sb.Append("""<pageMargins left="0.4" right="0.4" top="0.6" bottom="0.6" header="0.3" footer="0.3"/>""");
        sb.Append($"""<pageSetup paperSize="9" orientation="{(landscape ? "landscape" : "portrait")}" fitToWidth="1" fitToHeight="0"/>""");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    // ---- paket parçaları ----

    private static string ContentTypes(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
        sb.Append("""<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>""");
        sb.Append("""<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""");
        for (var i = 1; i <= sheetCount; i++)
            sb.Append($"""<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
        sb.Append("""<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        sb.Append("""<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>""");
        sb.Append("""<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>""");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private const string RootRels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/></Relationships>""";

    private static string Core(ReportDocument doc)
    {
        var created = doc.GeneratedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:dcmitype="http://purl.org/dc/dcmitype/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:title>{X(doc.Title)}</dc:title><dc:creator>{X(doc.Organization ?? "OnderCode Suite")}</dc:creator><dcterms:created xsi:type="dcterms:W3CDTF">{created}</dcterms:created><dcterms:modified xsi:type="dcterms:W3CDTF">{created}</dcterms:modified></cp:coreProperties>""";
    }

    private static string App(IReadOnlyList<string> sheetNames)
    {
        var titles = string.Concat(sheetNames.Select(n => $"<vt:lpstr>{X(n)}</vt:lpstr>"));
        return $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"><Application>OnderCode Suite</Application><HeadingPairs><vt:vector size="2" baseType="variant"><vt:variant><vt:lpstr>Çalışma Sayfaları</vt:lpstr></vt:variant><vt:variant><vt:i4>{sheetNames.Count}</vt:i4></vt:variant></vt:vector></HeadingPairs><TitlesOfParts><vt:vector size="{sheetNames.Count}" baseType="lpstr">{titles}</vt:vector></TitlesOfParts></Properties>""";
    }

    private static string Workbook(IReadOnlyList<(string Name, string Xml, string? FilterRange)> sheets)
    {
        var sb = new StringBuilder();
        sb.Append($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="{MainNs}" xmlns:r="{RelNs}"><bookViews><workbookView activeTab="0"/></bookViews><sheets>""");
        for (var i = 0; i < sheets.Count; i++)
            sb.Append($"""<sheet name="{X(sheets[i].Name)}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""");
        sb.Append("</sheets>");
        var filters = sheets.Select((s, i) => (s, i)).Where(x => x.s.FilterRange is not null).ToList();
        if (filters.Count > 0)
        {
            sb.Append("<definedNames>");
            foreach (var (s, i) in filters)
            {
                var abs = string.Join(':', s.FilterRange!.Split(':').Select(Absolute));
                sb.Append($"""<definedName name="_xlnm._FilterDatabase" localSheetId="{i}" hidden="1">{X("'" + s.Name.Replace("'", "''") + "'!" + abs)}</definedName>""");
            }
            sb.Append("</definedNames>");
        }
        sb.Append("""<calcPr calcId="191029" fullCalcOnLoad="1"/></workbook>""");
        return sb.ToString();
    }

    private static string WorkbookRels(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        for (var i = 1; i <= sheetCount; i++)
            sb.Append($"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""");
        sb.Append($"""<Relationship Id="rId{sheetCount + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    // ---- yardımcılar ----

    private static void Write(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        s.Write(bytes);
    }

    /// <summary>Sayfa adı: en çok 31 karakter, : \ / ? * [ ] yok, benzersiz.</summary>
    internal static string UniqueName(string name, HashSet<string> used)
    {
        var clean = string.Concat(name.Select(ch => ch is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? '-' : ch)).Trim().Trim('\'');
        if (clean.Length == 0) clean = "Sayfa";
        if (clean.Length > 31) clean = clean[..31].TrimEnd();
        var candidate = clean;
        for (var i = 2; !used.Add(candidate); i++)
        {
            var suffix = $" ({i})";
            candidate = (clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] : clean) + suffix;
        }
        return candidate;
    }

    internal static string Ref(int row, int col) => ColumnName(col) + row.ToString(CultureInfo.InvariantCulture);

    internal static string ColumnName(int col)
    {
        var name = "";
        for (var c = col + 1; c > 0; c = (c - 1) / 26) name = (char)('A' + (c - 1) % 26) + name;
        return name;
    }

    private static string Absolute(string cell)
    {
        var i = 0;
        while (i < cell.Length && char.IsAsciiLetter(cell[i])) i++;
        return "$" + cell[..i] + "$" + cell[i..];
    }

    internal static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>XML kaçışı; XML 1.0'da geçersiz kontrol karakterlerini atar.</summary>
    internal static string X(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\t' or '\n' or '\r': sb.Append(ch); break;
                case < ' ' or '￾' or '￿': break;
                default:
                    if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { sb.Append(ch).Append(text[++i]); }
                    else if (!char.IsSurrogate(ch)) sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Satır satır hücre biriktirir; satırlar ve hücreler sıralı yazılır.</summary>
    private sealed class SheetBuilder
    {
        private readonly SortedDictionary<int, SortedDictionary<int, string>> _rows = [];
        private readonly Dictionary<int, double> _heights = [];

        public int LastRow => _rows.Count == 0 ? 1 : _rows.Keys.Max();
        public int LastColumn => _rows.Count == 0 ? 0 : _rows.Values.Max(r => r.Count == 0 ? 0 : r.Keys.Max());

        public void Height(int row, double height) => _heights[row] = height;

        public void Text(int row, int col, string text, int style)
        {
            if (text.Length > 32767) text = text[..32767];
            Put(row, col, $"""<c r="{Ref(row, col)}" s="{style}" t="inlineStr"><is><t xml:space="preserve">{X(text)}</t></is></c>""");
        }

        public void Formula(int row, int col, string formula, int style)
            => Put(row, col, $"""<c r="{Ref(row, col)}" s="{style}"><f>{X(formula)}</f></c>""");

        public void Value(int row, int col, object? value, ReportColumn column, bool total)
        {
            var style = Styles.For(column, total);
            switch (value)
            {
                case null:
                    Put(row, col, $"""<c r="{Ref(row, col)}" s="{style}"/>""");
                    break;
                case string s:
                    Text(row, col, s, column.IsNumeric ? Styles.TextRight : Styles.Text);
                    break;
                case DateOnly d:
                    Number(row, col, d.ToDateTime(TimeOnly.MinValue).ToOADate(), style);
                    break;
                case DateTime dt:
                    Number(row, col, dt.ToOADate(), style);
                    break;
                case decimal m:
                    Put(row, col, $"""<c r="{Ref(row, col)}" s="{style}"><v>{m.ToString(CultureInfo.InvariantCulture)}</v></c>""");
                    break;
                case double dbl:
                    if (double.IsFinite(dbl)) Number(row, col, dbl, style);
                    else Put(row, col, $"""<c r="{Ref(row, col)}" s="{style}"/>""");
                    break;
                case float or int or long or short or byte:
                    Number(row, col, Convert.ToDouble(value, CultureInfo.InvariantCulture), style);
                    break;
                default:
                    Text(row, col, Convert.ToString(value, ReportFormat.Turkish) ?? "", Styles.Text);
                    break;
            }
        }

        private void Number(int row, int col, double v, int style)
            => Put(row, col, $"""<c r="{Ref(row, col)}" s="{style}"><v>{v.ToString("R", CultureInfo.InvariantCulture)}</v></c>""");

        private void Put(int row, int col, string xml)
        {
            if (!_rows.TryGetValue(row, out var cells)) _rows[row] = cells = [];
            cells[col] = xml;
        }

        public void AppendRows(StringBuilder sb)
        {
            foreach (var (row, cells) in _rows)
            {
                sb.Append($"""<row r="{row}" """);
                if (_heights.TryGetValue(row, out var h)) sb.Append($"""ht="{N(h)}" customHeight="1" """);
                sb.Append('>');
                foreach (var cell in cells.Values) sb.Append(cell);
                sb.Append("</row>");
            }
        }
    }

    /// <summary>styles.xml ve hücre biçimi dizinleri.</summary>
    private static class Styles
    {
        public const int Text = 1, TextRight = 2, Title = 3, Muted = 4, Header = 5, HeaderRight = 6, Bold = 7, TotalLabel = 8;
        private const int Integer = 9, Num0 = 10, Num1 = 11, Num2 = 12, Num3 = 13, Num4 = 14, Money = 15, Pct0 = 16, Pct1 = 17, Pct2 = 18, Date = 19, DateTime = 20;
        private const int TotalOffset = 12; // 21..32: aynı biçimlerin kalın + üst çizgili hâli

        public static int For(ReportColumn c, bool total)
        {
            var baseStyle = c.Kind switch
            {
                ColumnKind.Integer => Integer,
                ColumnKind.Number => c.Decimals switch { 0 => Num0, 1 => Num1, 2 => Num2, 3 => Num3, _ => Num4 },
                ColumnKind.Money => Money,
                ColumnKind.Percent => c.Decimals switch { 0 => Pct0, 1 => Pct1, _ => Pct2 },
                ColumnKind.Date => Date,
                ColumnKind.DateTime => DateTime,
                _ => Text,
            };
            if (!total) return baseStyle;
            return baseStyle >= Integer ? baseStyle + TotalOffset : TotalLabel;
        }

        // numFmt: 164 para, 165..167 ondalık, 168 tarih, 169 tarih-saat, 170 yüzde tek hane, 171 dört hane
        private static readonly (int NumFmt, int Font, int Fill, int Border, string? Align)[] Formats =
        [
            (0, 0, 0, 0, null),          // 0 varsayılan
            (0, 0, 0, 0, null),          // 1 metin
            (0, 0, 0, 0, "right"),       // 2 sağa yaslı metin
            (0, 2, 0, 0, null),          // 3 başlık
            (0, 3, 0, 0, null),          // 4 soluk
            (0, 1, 2, 1, null),          // 5 sütun başlığı
            (0, 1, 2, 1, "right"),       // 6 sütun başlığı, sağa yaslı
            (0, 1, 0, 0, null),          // 7 kalın
            (0, 1, 0, 2, null),          // 8 toplam etiketi
            (3, 0, 0, 0, null),          // 9 tam sayı  #,##0
            (3, 0, 0, 0, null),          // 10 0 hane
            (165, 0, 0, 0, null),        // 11 1 hane
            (4, 0, 0, 0, null),          // 12 2 hane   #,##0.00
            (166, 0, 0, 0, null),        // 13 3 hane
            (171, 0, 0, 0, null),        // 14 4 hane
            (164, 0, 0, 0, null),        // 15 para
            (9, 0, 0, 0, null),          // 16 yüzde 0 hane
            (170, 0, 0, 0, null),        // 17 yüzde 1 hane
            (10, 0, 0, 0, null),         // 18 yüzde 2 hane
            (168, 0, 0, 0, null),        // 19 tarih
            (169, 0, 0, 0, null),        // 20 tarih-saat
        ];

        public static readonly string Xml = Build();

        private static string Build()
        {
            var sb = new StringBuilder();
            sb.Append($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="{MainNs}">""");
            sb.Append("""<numFmts count="8">""");
            sb.Append("""<numFmt numFmtId="164" formatCode="#,##0.00\ &quot;₺&quot;"/>""");
            sb.Append("""<numFmt numFmtId="165" formatCode="#,##0.0"/>""");
            sb.Append("""<numFmt numFmtId="166" formatCode="#,##0.000"/>""");
            sb.Append("""<numFmt numFmtId="167" formatCode="#,##0"/>""");
            sb.Append("""<numFmt numFmtId="168" formatCode="dd\.mm\.yyyy"/>""");
            sb.Append("""<numFmt numFmtId="169" formatCode="dd\.mm\.yyyy\ hh:mm"/>""");
            sb.Append("""<numFmt numFmtId="170" formatCode="0.0%"/>""");
            sb.Append("""<numFmt numFmtId="171" formatCode="#,##0.0000"/>""");
            sb.Append("</numFmts>");
            sb.Append("""<fonts count="4">""");
            sb.Append("""<font><sz val="11"/><color rgb="FF1E293B"/><name val="Calibri"/><family val="2"/></font>""");
            sb.Append("""<font><b/><sz val="11"/><color rgb="FF0F172A"/><name val="Calibri"/><family val="2"/></font>""");
            sb.Append("""<font><b/><sz val="16"/><color rgb="FF0F172A"/><name val="Calibri"/><family val="2"/></font>""");
            sb.Append("""<font><sz val="10"/><color rgb="FF64748B"/><name val="Calibri"/><family val="2"/></font>""");
            sb.Append("</fonts>");
            sb.Append("""<fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEEF2F7"/><bgColor indexed="64"/></patternFill></fill></fills>""");
            sb.Append("""<borders count="3">""");
            sb.Append("""<border><left/><right/><top/><bottom/><diagonal/></border>""");
            sb.Append("""<border><left/><right/><top/><bottom style="thin"><color rgb="FFCBD5E1"/></bottom><diagonal/></border>""");
            sb.Append("""<border><left/><right/><top style="thin"><color rgb="FF94A3B8"/></top><bottom/><diagonal/></border>""");
            sb.Append("</borders>");
            sb.Append("""<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>""");

            var all = Formats.ToList();
            for (var i = Integer; i <= DateTime; i++)
            {
                var f = Formats[i];
                all.Add((f.NumFmt, 1, 0, 2, f.Align));
            }
            sb.Append($"""<cellXfs count="{all.Count}">""");
            foreach (var (numFmt, font, fill, border, align) in all)
            {
                sb.Append($"""<xf numFmtId="{numFmt}" fontId="{font}" fillId="{fill}" borderId="{border}" xfId="0" """);
                if (numFmt != 0) sb.Append("""applyNumberFormat="1" """);
                if (font != 0) sb.Append("""applyFont="1" """);
                if (fill != 0) sb.Append("""applyFill="1" """);
                if (border != 0) sb.Append("""applyBorder="1" """);
                if (align is null) sb.Append("/>");
                else sb.Append($"""applyAlignment="1"><alignment horizontal="{align}"/></xf>""");
            }
            sb.Append("</cellXfs>");
            sb.Append("""<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>""");
            sb.Append("</styleSheet>");
            return sb.ToString();
        }
    }
}
