namespace Eczane.WinForms.Pages;

/// <summary>Miad (SKT) takibi: süresi geçmiş, kritik (30 gün) ve izlemedeki (90 gün) partiler; satış hızına göre kaç kutunun elde kalacağı tahmini, imha ve sayım.</summary>
sealed class ExpiryPage : PageBase
{
    static readonly int[] Windows = [30, 90, 180];
    readonly OndaSegmented _window = new() { Items = ["30 gün", "90 gün", "180 gün"], SelectedIndex = 1 };
    readonly OndaButton _dispose = new() { Text = "İmha et", Icon = "\uE74D", Kind = OndaButtonKind.Outline, Width = 130 };
    readonly OndaButton _count = new() { Text = "Sayım", Icon = "\uE73E", Kind = OndaButtonKind.Outline, Width = 110 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 100 };
    readonly Stat _expired = new() { Title = "SKT geçmiş", Icon = "\uE7BA", IconColor = Color.FromArgb(220, 38, 38), Prefix = "₺" };
    readonly Stat _critical = new() { Title = "Kritik · 30 gün", Icon = "\uE823", IconColor = App.Amber, Prefix = "₺" };
    readonly Stat _watch = new() { Title = "İzlemede · 90 gün", Icon = "\uE7B3", IconColor = App.Cyan, Prefix = "₺" };
    readonly Stat _risk = new() { Title = "Satılamayacak (tahmin)", Icon = "\uE74D", IconColor = App.Violet };
    readonly OndaGrid _grid = new() { RowHeight = 34, AutoGenerateColumns = false };

    public ExpiryPage() : base("Miad takibi", "Yükleniyor…")
    {
        _grid.Columns.AddRange(
        [
            Col.Day(nameof(ExpiryRow.Expiry), "SKT", 105),
            Col.Int(nameof(ExpiryRow.DaysLeft), "Kalan gün", 90),
            Col.Badge(nameof(ExpiryRow.LevelText), "Durum", 100),
            Col.Text(nameof(ExpiryRow.Product), "Ürün", 270),
            Col.Text(nameof(ExpiryRow.Barcode), "Barkod", 130),
            Col.Text(nameof(ExpiryRow.LotNo), "Parti", 100),
            Col.Int(nameof(ExpiryRow.Quantity), "Kutu", 70, true),
            Col.Money(nameof(ExpiryRow.Value), "Maliyet", 110, true),
            Col.Dbl(nameof(ExpiryRow.DailyUsage), "Günlük satış", 100, "0.00"),
            Col.Int(nameof(ExpiryRow.DaysToSellOut), "Biter (gün)", 90),
            Col.Int(nameof(ExpiryRow.AtRisk), "Elde kalır", 90, true),
            Col.Text(nameof(ExpiryRow.Advice), "Öneri", 220),
            Col.Text(nameof(ExpiryRow.Supplier), "Depo", 150),
        ]);
        _grid.RowTint = o => o is ExpiryRow { Level: ExpiryLevel.Expired } ? Color.FromArgb(30, 239, 68, 68) : o is ExpiryRow { Level: ExpiryLevel.Critical } ? Color.FromArgb(26, 245, 158, 11) : null;
        _window.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _dispose.Click += async (_, _) => await DisposeAsync();
        _count.Click += async (_, _) => await CountAsync();
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog("Miad takibi");
        _grid.RowActivated += async (_, e) => { if (e.Item is ExpiryRow row) await ProductsPage.ShowCardAsync(FindForm(), row.ProductId); };
        Controls.AddRange(new Control[] { _window, _dispose, _count, _excel, _expired, _critical, _watch, _risk, _grid });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new ListExpiry(Windows[Math.Max(0, _window.SelectedIndex)]));
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var rows = result.Value;
        _grid.DataSource = rows;
        Set(_expired, rows.Where(r => r.Level == ExpiryLevel.Expired).ToList(), "imha ya da depoya iade");
        Set(_critical, rows.Where(r => r.Level == ExpiryLevel.Critical).ToList(), "30 gün içinde");
        Set(_watch, rows.Where(r => r.Level == ExpiryLevel.Watch).ToList(), "31-90 gün");
        _risk.Value = rows.Sum(r => r.AtRisk);
        _risk.DeltaText = $"kutu · değeri {App.Money(rows.Sum(r => r.AtRisk * r.UnitCost))}";
        Subtitle = $"{rows.Count} parti  ·  SKT'si en yakın parti önce satılır (FEFO)  ·  satış hızı son 30 güne göre  ·  ürün kartı için çift tıklayın";
        foreach (var c in new[] { _expired, _critical, _watch, _risk }) c.Replay();
    }

    static void Set(Stat card, List<ExpiryRow> rows, string note)
    {
        card.Value = rows.Sum(r => r.Value);
        card.DeltaText = $"{rows.Count} parti · {rows.Sum(r => r.Quantity)} kutu · {note}";
    }

    async Task DisposeAsync()
    {
        if (_grid.SelectedItem is not ExpiryRow row) { OndaToast.Info("Önce bir parti seçin."); return; }
        var reason = row.Level == ExpiryLevel.Expired ? "SKT geçti" : "Hasarlı ambalaj";
        if (InputForm.Ask(FindForm(), $"İmha · {row.Product}", "\uE74D", "İmha edilecek kutu", row.Quantity.ToString(), $"Parti {row.LotNo} · SKT {row.Expiry:dd.MM.yyyy} · gerekçe: {reason}", OndaInputKind.Integer,
            async text =>
            {
                var r = await App.Send(new DisposeLot(row.LotId, int.TryParse(text, out var q) ? q : 0, reason));
                if (!r.IsSuccess) { App.ShowErrors(r, "İmha kaydedilemedi"); return false; }
                return true;
            }))
        {
            OndaToast.Success($"{row.Product} · parti {row.LotNo}", "İmha kaydedildi");
            await ReloadAsync();
        }
    }

    async Task CountAsync()
    {
        if (_grid.SelectedItem is not ExpiryRow row) { OndaToast.Info("Önce bir parti seçin."); return; }
        var diff = 0;
        if (InputForm.Ask(FindForm(), $"Sayım · {row.Product}", "\uE73E", "Rafta sayılan kutu", row.Quantity.ToString(), $"Parti {row.LotNo} · sistemde {row.Quantity} kutu", OndaInputKind.Integer,
            async text =>
            {
                var r = await App.Send(new CountLot(row.LotId, int.TryParse(text, out var q) ? q : -1));
                if (!r.IsSuccess) { App.ShowErrors(r, "Sayım kaydedilemedi"); return false; }
                diff = r.Value;
                return true;
            }))
        {
            OndaToast.Success(diff == 0 ? "Fark yok." : $"Sayım farkı: {diff:+0;-0} kutu", "Sayım kaydedildi");
            await ReloadAsync();
        }
    }

    protected override void LayoutPage(Rectangle r)
    {
        _window.SetBounds(r.X, r.Y, 330, 38);
        RightAlign(r.Right, r.Y, _excel, _count, _dispose);
        var y = r.Y + 52;
        var cards = new[] { _expired, _critical, _watch, _risk };
        var cw = (r.Width - Gap * 3) / 4;
        for (var i = 0; i < 4; i++) cards[i].SetBounds(r.X + i * (cw + Gap), y, cw, 140);
        y += 140 + Gap;
        _grid.SetBounds(r.X, y, r.Width, r.Bottom - y);
    }
}
