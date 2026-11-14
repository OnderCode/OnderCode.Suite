using Donanim.WinForms.Controls;
using OnderCode.Reporting.Export;

namespace Donanim.WinForms.Pages;

/// <summary>Öneriler ve rapor: bütün öneriler, alışveriş listesi, PDF/Excel karne ve ikinci el ilanı metni.</summary>
sealed class AdvicePage : KarnePage
{
    readonly OndaButton _pdf = new() { Text = "PDF karne", Icon = Glyph.Document, Kind = OndaButtonKind.Primary, Width = 140 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 120 };
    readonly OndaButton _copy = new() { Text = "İlan metnini kopyala", Icon = Glyph.Copy, Kind = OndaButtonKind.Soft, Width = 200 };
    readonly OndaPanel _host = ScrollHost();
    readonly AdviceList _advice = new() { Title = "Bütün öneriler", Icon = Glyph.Bulb, IconColor = OndaTheme.Colors.Warning };
    readonly ShoppingList _shopping = new();
    readonly TextTile _listing = new() { Title = "İkinci el ilanı için metin", Icon = Glyph.Document, IconColor = App.Teal };

    public AdvicePage() : base("Öneriler ve rapor", "Yükleniyor…")
    {
        _host.Controls.Add(_advice);
        _pdf.Click += async (_, _) => { if (App.Karne is { } k) await ReportSaver.SaveAsync(FindForm(), KarneReport.Build(k), ReportExporters.Pdf); };
        _excel.Click += async (_, _) => { if (App.Karne is { } k) await ReportSaver.SaveAsync(FindForm(), KarneReport.Build(k), ReportExporters.Excel); };
        _copy.Click += (_, _) =>
        {
            if (App.Karne is not { } k) return;
            Clipboard.SetText(KarneReport.Listing(k));
            OndaToast.Success("İlan metni panoya kopyalandı.", "Kopyalandı");
        };
        Controls.AddRange(new Control[] { _pdf, _excel, _copy, _host, _shopping, _listing });
    }

    protected override void Bind(Karne k)
    {
        var s = k.Snapshot;
        Subtitle = $"{MachineLine(s)}  ·  {k.Advice.Count} öneri, {k.Shopping.Count} parça";
        _advice.Items = k.Advice;
        _advice.Note = $"{k.CriticalCount} önemli · {k.RecommendedCount} önerilen";
        _shopping.Items = k.Shopping;
        _listing.Body = KarneReport.Listing(k);
    }

    protected override void LayoutPage(Rectangle r)
    {
        RightAlign(r.Right, 26, _copy, _excel, _pdf);
        foreach (var b in new[] { _pdf, _excel, _copy }) b.Height = 36;
        var lw = (int)(r.Width * 0.6);
        _host.SetBounds(r.X, r.Y, lw, r.Height);
        var need = _advice.PreferredHeightFor(lw);
        var w = need > r.Height ? lw - SystemInformation.VerticalScrollBarWidth - 4 : lw;
        need = _advice.PreferredHeightFor(w);
        _advice.SetBounds(0, _host.AutoScrollPosition.Y, w, Math.Max(need, r.Height));
        var rx = r.X + lw + Gap;
        var rw = r.Width - lw - Gap;
        var sh = Math.Min(Math.Max(170, 52 * Math.Max(1, _shopping.Items.Count) + 90), r.Height / 2);
        _shopping.SetBounds(rx, r.Y, rw, sh);
        _listing.SetBounds(rx, r.Y + sh + Gap, rw, r.Height - sh - Gap);
    }
}
