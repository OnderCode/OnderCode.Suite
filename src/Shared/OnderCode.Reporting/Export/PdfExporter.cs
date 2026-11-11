using System.Globalization;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OnderCode.Reporting.Export;

/// <summary>
/// A4 PDF raporu: başlık bandı, gösterge kutuları, grafikler, sayfalara bölünen tablolar (başlık satırı her sayfada tekrar eder),
/// notlar ve "Sayfa x / y" altlığı. PDFsharp 6.2.4 (MIT) ile çizilir; yazı tipi PDF'e gömülür.
/// </summary>
public sealed class PdfExporter : IReportExporter
{
    public string Name => "PDF";
    public string Extension => ".pdf";

    public void Export(ReportDocument document, Stream output)
    {
        ArgumentNullException.ThrowIfNull(document);
        SystemFontResolver.EnsureRegistered();
        new Writer(document).Write(output);
    }

    private sealed class Writer(ReportDocument doc)
    {
        private const double Margin = 40, TopMargin = 38, FooterSpace = 44;
        private const double HeadH = 18, RowH = 15.5;

        private static readonly XColor Ink = Hex("#0F172A"), TextColor = Hex("#1E293B"), Muted = Hex("#64748B"), Faint = Hex("#94A3B8");
        private static readonly XColor Border = Hex("#E2E8F0"), HeaderFill = Hex("#F1F5F9"), Zebra = Hex("#F8FAFC"), Accent = Hex("#2563EB");
        private static readonly string[] Palette = ["#2563EB", "#14B8A6", "#F59E0B", "#EC4899", "#8B5CF6", "#64748B"];

        private static readonly XPdfFontOptions Unicode = new(PdfFontEncoding.Unicode);
        private static readonly XFont FTitle = Font(19, true), FSub = Font(9.5), FOrg = Font(7.5, true), FH2 = Font(11, true);
        private static readonly XFont FBody = Font(8.3), FBold = Font(8.3, true), FHead = Font(7.4, true), FSmall = Font(7.2);
        private static readonly XFont[] FKpiValues = [Font(15, true), Font(13, true), Font(11.5, true), Font(10, true)];
        private static readonly XFont FKpiLabel = Font(7.4);

        private readonly PdfDocument _pdf = new();
        private XGraphics _g = null!;
        private double _y, _w, _h;

        private double Left => Margin;
        private double Right => _w - Margin;
        private double Width => _w - Margin * 2;
        private double Bottom => _h - FooterSpace;

        public void Write(Stream output)
        {
            using (_pdf)
            {
                _pdf.Info.Title = doc.Title;
                _pdf.Info.Author = doc.Organization ?? "OnderCode Suite";
                _pdf.Info.Subject = doc.Subtitle ?? doc.Title;
                _pdf.Info.Creator = "OnderCode Suite";

                NewPage();
                Header();
                Kpis();
                foreach (var chart in doc.Charts) Chart(chart);
                foreach (var table in doc.Tables) Table(table);
                Notes();
                _g.Dispose();

                Footers();
                _pdf.Save(output, false);
            }
        }

        // ---- sayfa ----

        private void NewPage()
        {
            _g?.Dispose();
            var page = _pdf.AddPage();
            page.Size = PageSize.A4;
            if (doc.Landscape) page.Orientation = PageOrientation.Landscape;
            _g = XGraphics.FromPdfPage(page);
            _w = _g.PageSize.Width;
            _h = _g.PageSize.Height;
            _y = TopMargin;
            if (_pdf.PageCount > 1)
            {
                Text(doc.Title, FSmall, Faint, Left, _y, Width * 0.7);
                if (doc.Organization is { } org) Text(org, FSmall, Faint, Right, _y, Width * 0.3, right: true);
                _y += 18;
            }
        }

        private void EnsureSpace(double height)
        {
            if (_y + height > Bottom) NewPage();
        }

        private void Header()
        {
            var top = _y;
            Text("Oluşturma: " + ReportFormat.DateTime(doc.GeneratedAt), FSmall, Muted, Right, top + 2, 160, right: true);
            if (!string.IsNullOrWhiteSpace(doc.Organization))
            {
                Text(doc.Organization.ToUpper(ReportFormat.Turkish), FOrg, Accent, Left, _y, Width - 170);
                _y += 13;
            }
            Text(doc.Title, FTitle, Ink, Left, _y, Width - (doc.Organization is null ? 170 : 0));
            _y += 26;
            if (!string.IsNullOrWhiteSpace(doc.Subtitle))
            {
                Text(doc.Subtitle, FSub, Muted, Left, _y, Width);
                _y += 15;
            }
            _y += 5;
            _g.DrawRectangle(new XSolidBrush(Accent), Left, _y, 44, 3);
            _g.DrawLine(new XPen(Border, 0.8), Left + 48, _y + 1.5, Right, _y + 1.5);
            _y += 18;
        }

        private void Footers()
        {
            var count = _pdf.PageCount;
            for (var i = 0; i < count; i++)
            {
                using var g = XGraphics.FromPdfPage(_pdf.Pages[i], XGraphicsPdfPageOptions.Append);
                var w = g.PageSize.Width;
                var y = g.PageSize.Height - 30;
                g.DrawLine(new XPen(Border, 0.6), Margin, y, w - Margin, y);
                var left = string.Join("  ·  ", new[] { doc.Organization, doc.Title }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var leftText = Fit(g, left, FSmall, (w - Margin * 2) * 0.7);
                g.DrawString(leftText, FSmall, new XSolidBrush(Faint), Margin, y + 6, XStringFormats.TopLeft);
                var right = $"OnderCode Suite  ·  Sayfa {i + 1} / {count}";
                var rw = g.MeasureString(right, FSmall).Width;
                g.DrawString(right, FSmall, new XSolidBrush(Faint), w - Margin - rw, y + 6, XStringFormats.TopLeft);
            }
        }

        // ---- göstergeler ----

        private void Kpis()
        {
            var kpis = doc.Kpis;
            if (kpis.Count == 0) return;
            const double tileH = 54, gap = 10;
            var perRow = Math.Min(doc.Landscape ? 5 : 4, kpis.Count);
            var tileW = (Width - gap * (perRow - 1)) / perRow;
            for (var i = 0; i < kpis.Count; i += perRow)
            {
                EnsureSpace(tileH + gap);
                for (var j = 0; j < perRow && i + j < kpis.Count; j++)
                {
                    var k = kpis[i + j];
                    var x = Left + j * (tileW + gap);
                    _g.DrawRoundedRectangle(new XPen(Border, 0.8), new XSolidBrush(Zebra), x, _y, tileW, tileH, 8, 8);
                    Text(k.Label, FKpiLabel, Muted, x + 10, _y + 8, tileW - 20);
                    // Uzun tutarlar kesilmesin: sığana kadar küçült
                    var valueFont = FKpiValues.FirstOrDefault(f => _g.MeasureString(k.Value, f).Width <= tileW - 20) ?? FKpiValues[^1];
                    Text(k.Value, valueFont, Ink, x + 10, _y + 19 + (FKpiValues[0].Size - valueFont.Size) * 0.6, tileW - 20);
                    if (k.Note is { Length: > 0 } note) Text(note, FSmall, Faint, x + 10, _y + 39, tileW - 20);
                }
                _y += tileH + gap;
            }
            _y += 8;
        }

        // ---- grafik ----

        private void Chart(ReportChart chart)
        {
            var n = chart.Labels.Count;
            if (n == 0 || chart.Series.Count == 0) return;
            const double h = 205;
            EnsureSpace(h + 16);
            var top = _y;
            _g.DrawRoundedRectangle(new XPen(Border, 0.8), XBrushes.White, Left, top, Width, h, 10, 10);
            Text(chart.Title, FH2, Ink, Left + 12, top + 10, Width * 0.55);
            if (chart.Subtitle is { Length: > 0 } sub) Text(sub, FSmall, Muted, Left + 12, top + 25, Width * 0.55);

            // açıklama (lejant) sağ üstte
            var lx = Right - 12;
            for (var s = chart.Series.Count - 1; s >= 0 && chart.Series.Count > 1; s--)
            {
                var series = chart.Series[s];
                var tw = _g.MeasureString(series.Name, FSmall).Width;
                lx -= tw;
                Text(series.Name, FSmall, Muted, lx, top + 12, tw + 1);
                lx -= 16;
                var pen = new XPen(SeriesColor(chart, s), 2) { DashStyle = series.Dashed ? XDashStyle.Dash : XDashStyle.Solid };
                if (chart.Kind == ChartKind.Bar) _g.DrawRectangle(new XSolidBrush(SeriesColor(chart, s)), lx + 2, top + 13.5, 9, 7);
                else _g.DrawLine(pen, lx, top + 17, lx + 12, top + 17);
                lx -= 12;
            }

            double px = Left + 50, py = top + 46, pw = Width - 50 - 16, ph = h - 46 - 28;
            var values = chart.Series.SelectMany(s => s.Values).Where(double.IsFinite).ToList();
            var min = chart.Min ?? Math.Min(0, values.DefaultIfEmpty(0).Min());
            var max = chart.Max ?? NiceMax(values.DefaultIfEmpty(1).Max(), min);
            if (max <= min) max = min + 1;
            var format = chart.ValueFormat ?? ReportFormat.Compact;
            double Y(double v) => Math.Clamp(py + ph - (v - min) / (max - min) * ph, py, py + ph);
            double X(int i) => chart.Kind == ChartKind.Bar ? px + pw * (i + 0.5) / n : n <= 1 ? px + pw / 2 : px + pw * i / (n - 1);

            var grid = new XPen(Border, 0.6) { DashStyle = XDashStyle.Dash };
            for (var i = 0; i <= 4; i++)
            {
                var gy = py + ph - ph * i / 4;
                _g.DrawLine(i == 0 ? new XPen(Faint, 0.7) : grid, px, gy, px + pw, gy);
                Text(format(min + (max - min) * i / 4), FSmall, Muted, px - 6, gy - 4.5, 44, right: true);
            }
            var step = Math.Max(1, (int)Math.Ceiling(n / (pw / 44)));
            for (var i = 0; i < n; i += step)
                Text(chart.Labels[i], FSmall, Muted, X(i), py + ph + 6, 42, center: true);

            if (chart.MarkerIndex is int m && m >= 0 && m < n)
            {
                var mx = chart.Kind == ChartKind.Bar ? px + pw * m / n : X(m);
                _g.DrawLine(new XPen(Faint, 0.8) { DashStyle = XDashStyle.Dot }, mx, py - 4, mx, py + ph);
                if (chart.MarkerLabel is { Length: > 0 } ml) Text(ml, FSmall, Muted, mx + 4, py - 6, 80);
            }

            if (chart.Kind == ChartKind.Bar)
            {
                var band = pw / n;
                var groupW = Math.Min(band * 0.72, chart.Series.Count * 14);
                var bw = groupW / chart.Series.Count;
                var baseY = Y(Math.Max(min, 0));
                for (var s = 0; s < chart.Series.Count; s++)
                {
                    var brush = new XSolidBrush(SeriesColor(chart, s));
                    var vals = chart.Series[s].Values;
                    for (var i = 0; i < Math.Min(n, vals.Count); i++)
                    {
                        if (!double.IsFinite(vals[i])) continue;
                        var x = X(i) - groupW / 2 + s * bw + 0.8;
                        var y = Y(vals[i]);
                        var hh = Math.Abs(baseY - y);
                        if (hh < 0.3) continue;
                        _g.DrawRectangle(brush, x, Math.Min(y, baseY), Math.Max(1, bw - 1.6), hh);
                    }
                }
            }
            else
            {
                for (var s = 0; s < chart.Series.Count; s++)
                {
                    var series = chart.Series[s];
                    var color = SeriesColor(chart, s);
                    var pen = new XPen(color, 1.8) { LineJoin = XLineJoin.Round, LineCap = XLineCap.Round, DashStyle = series.Dashed ? XDashStyle.Dash : XDashStyle.Solid };
                    var segment = new List<XPoint>();
                    void Flush()
                    {
                        if (segment.Count >= 2) _g.DrawLines(pen, segment.ToArray());
                        else if (segment.Count == 1) _g.DrawEllipse(new XSolidBrush(color), segment[0].X - 1.8, segment[0].Y - 1.8, 3.6, 3.6);
                        segment.Clear();
                    }
                    for (var i = 0; i < Math.Min(n, series.Values.Count); i++)
                    {
                        if (double.IsFinite(series.Values[i])) segment.Add(new XPoint(X(i), Y(series.Values[i])));
                        else Flush();
                    }
                    Flush();
                }
            }
            _y = top + h + 14;
        }

        // ---- tablo ----

        private void Table(ReportTable table)
        {
            var cols = table.Columns;
            var total = cols.Sum(c => c.Width);
            var widths = cols.Select(c => Width * c.Width / total).ToArray();

            EnsureSpace(30 + HeadH + RowH * 3);
            Text(table.Title, FH2, Ink, Left, _y, Width);
            _y += 17;
            if (table.Description is { Length: > 0 } description)
            {
                foreach (var line in Wrap(description, FSmall, Width))
                {
                    Text(line, FSmall, Muted, Left, _y, Width);
                    _y += 10;
                }
                _y += 3;
            }
            HeaderRow(cols, widths);

            if (table.Rows.Count == 0)
            {
                Text("Kayıt yok", FBody, Muted, Left + 5, _y + 3.5, Width - 10);
                _y += RowH;
            }
            for (var r = 0; r < table.Rows.Count; r++)
            {
                if (_y + RowH > Bottom)
                {
                    NewPage();
                    Text(table.Title + " (devam)", FBold, Muted, Left, _y, Width);
                    _y += 14;
                    HeaderRow(cols, widths);
                }
                if (r % 2 == 1) _g.DrawRectangle(new XSolidBrush(Zebra), Left, _y, Width, RowH);
                Cells(table.Rows[r], cols, widths, FBody, TextColor);
                _y += RowH;
            }
            _g.DrawLine(new XPen(Border, 0.8), Left, _y, Right, _y);

            if (table.HasTotals && table.Rows.Count > 0)
            {
                if (_y + RowH + 2 > Bottom)
                {
                    NewPage();
                    HeaderRow(cols, widths);
                }
                var totals = table.Totals();
                if (!cols[0].Total) totals[0] = "Toplam";
                _g.DrawLine(new XPen(Faint, 0.9), Left, _y, Right, _y);
                Cells(totals, cols, widths, FBold, Ink);
                _y += RowH;
            }
            _y += 16;
        }

        private void HeaderRow(IReadOnlyList<ReportColumn> cols, double[] widths)
        {
            _g.DrawRectangle(new XSolidBrush(HeaderFill), Left, _y, Width, HeadH);
            var x = Left;
            for (var c = 0; c < cols.Count; c++)
            {
                var cw = widths[c];
                if (cols[c].IsNumeric) Text(cols[c].Header, FHead, Hex("#475569"), x + cw - 5, _y + 5, cw - 8, right: true);
                else Text(cols[c].Header, FHead, Hex("#475569"), x + 5, _y + 5, cw - 8);
                x += cw;
            }
            _y += HeadH;
        }

        private void Cells(object?[] row, IReadOnlyList<ReportColumn> cols, double[] widths, XFont font, XColor color)
        {
            var x = Left;
            for (var c = 0; c < cols.Count; c++)
            {
                var cw = widths[c];
                var text = row[c] is string s ? s : ReportFormat.Cell(row[c], cols[c]);
                if (cols[c].IsNumeric && row[c] is not string) Text(text, font, color, x + cw - 5, _y + 3.6, cw - 8, right: true);
                else Text(text, font, color, x + 5, _y + 3.6, cw - 8);
                x += cw;
            }
        }

        // ---- notlar ----

        private void Notes()
        {
            if (doc.Notes.Count == 0) return;
            EnsureSpace(44);
            Text("Notlar", FH2, Ink, Left, _y, Width);
            _y += 17;
            foreach (var note in doc.Notes)
            {
                var first = true;
                foreach (var line in Wrap(note, FBody, Width - 12))
                {
                    EnsureSpace(12);
                    if (first) Text("•", FBody, Accent, Left, _y, 10);
                    Text(line, FBody, TextColor, Left + 12, _y, Width - 12);
                    _y += 12;
                    first = false;
                }
                _y += 3;
            }
        }

        // ---- yardımcılar ----

        private void Text(string? text, XFont font, XColor color, double x, double y, double maxWidth, bool right = false, bool center = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            var fitted = Fit(_g, text, font, maxWidth);
            if (fitted.Length == 0) return;
            var w = _g.MeasureString(fitted, font).Width;
            var px = right ? x - w : center ? x - w / 2 : x;
            _g.DrawString(fitted, font, new XSolidBrush(color), px, y, XStringFormats.TopLeft);
        }

        /// <summary>Sığmayan metni "…" ile kısaltır.</summary>
        private static string Fit(XGraphics g, string text, XFont font, double maxWidth)
        {
            if (maxWidth <= 0) return "";
            if (g.MeasureString(text, font).Width <= maxWidth) return text;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (g.MeasureString(text[..mid].TrimEnd() + "…", font).Width <= maxWidth) lo = mid;
                else hi = mid - 1;
            }
            return lo == 0 ? "" : text[..lo].TrimEnd() + "…";
        }

        private List<string> Wrap(string text, XFont font, double width)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r", "").Split('\n'))
            {
                var line = "";
                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (_g.MeasureString(candidate, font).Width <= width || line.Length == 0) line = candidate;
                    else
                    {
                        lines.Add(line);
                        line = word;
                    }
                }
                lines.Add(line);
            }
            return lines;
        }

        private static XColor SeriesColor(ReportChart chart, int index)
            => Hex(chart.Series[index].Color ?? Palette[index % Palette.Length]);

        private static double NiceMax(double max, double min)
        {
            var range = max - min;
            if (range <= 0) return min + 1;
            var raw = range / 4;
            var exp = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            var f = raw / exp;
            var nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10;
            return min + nf * exp * 4;
        }

        private static XFont Font(double size, bool bold = false)
            => new("Segoe UI", size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular, Unicode);

        private static XColor Hex(string hex)
        {
            var h = hex.TrimStart('#');
            return XColor.FromArgb(
                int.Parse(h[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(h[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(h[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }
}
