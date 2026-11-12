using Otopark.Application;
using Otopark.Domain;
using Otopark.WinForms.Controls;

namespace Otopark.WinForms.Pages;

/// <summary>Yıkama kuyruğu: kartı seçip bir sonraki aşamaya geçirin (çift tıklama da olur). Tahmini bitiş 3 yıkama bölmesine göre hesaplanır.</summary>
sealed class WashPage : PageBase
{
    static readonly string[] Washers = ["Murat", "Ercan", "Yusuf", "Sinan"];
    readonly WashBoard _board = new();
    readonly OndaComboBox _washer = new() { Compact = true, Placeholder = "Yıkamacı", Width = 180 };
    readonly OndaButton _advance = new() { Text = "Sonraki aşama", Icon = "", Kind = OndaButtonKind.Success, Width = 180 };
    readonly OndaButton _cancel = new() { Text = "İptal", Icon = "", Kind = OndaButtonKind.Ghost, Width = 110 };
    readonly OndaButton _new = new() { Text = "Yeni sipariş", Icon = "", Shortcut = Keys.F4, Width = 170 };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 60_000 };

    public WashPage() : base("Yıkama kuyruğu", "Yükleniyor…")
    {
        foreach (var w in Washers) _washer.Items.Add(w);
        _washer.SelectedItem = Washers[0];
        _advance.Click += async (_, _) => await AdvanceAsync(_board.Selected);
        _cancel.Click += async (_, _) => await CancelAsync(_board.Selected);
        _new.Click += async (_, _) => { if (WashOrderForm.Open(FindForm()) is not null) await ReloadAsync(); };
        _board.CardActivated += async (_, c) => await AdvanceAsync(c);
        _board.CardSelected += (_, c) => UpdateButtons(c);
        _timer.Tick += async (_, _) => { if (Visible) await ReloadAsync(); };
        _timer.Start();
        Controls.AddRange(new Control[] { _board, _washer, _advance, _cancel, _new });
        UpdateButtons(null);
    }

    public WashBoard Board => _board;

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new GetWashBoard());
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var cards = result.Value;
        _board.SetCards(cards);
        var delivered = cards.Where(c => c.Status == WashStatus.Delivered).ToList();
        Subtitle = $"{cards.Count(c => c.Status == WashStatus.Waiting)} bekliyor  ·  {cards.Count(c => c.Status == WashStatus.Washing)} yıkanıyor  ·  " +
                   $"{cards.Count(c => c.Status == WashStatus.Ready)} hazır  ·  bugün {delivered.Count} araç, {delivered.Sum(c => c.Price):N0} ₺  ·  kartı çift tıklayın: sonraki aşama";
        UpdateButtons(_board.Selected);
    }

    void UpdateButtons(WashCard? c)
    {
        _advance.Enabled = c is not null && c.Status is WashStatus.Waiting or WashStatus.Washing || c is { Status: WashStatus.Ready, InLot: false };
        _advance.Text = c?.Status switch
        {
            WashStatus.Waiting => "Yıkamaya al",
            WashStatus.Washing => "Hazır",
            WashStatus.Ready when c is { InLot: true } => "Çıkışta teslim",
            WashStatus.Ready => "Teslim et",
            _ => "Sonraki aşama",
        };
        _cancel.Enabled = c?.Status == WashStatus.Waiting;
    }

    async Task AdvanceAsync(WashCard? c)
    {
        if (c is null) { OndaToast.Info("Önce bir kart seçin."); return; }
        if (c.Status == WashStatus.Ready && c.InLot)
        {
            OndaToast.Info($"{c.Plate} otoparkta ({c.SpotCode}); araç çıkarken teslim edilir ve ücret çıkışta alınır.");
            return;
        }
        var r = await App.Send(new AdvanceWash(c.Id, _washer.SelectedItem as string));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        OndaToast.Success($"{c.Plate}: {Texts.Of(r.Value).ToLower(Texts.Turkish)}");
        await ReloadAsync();
    }

    async Task CancelAsync(WashCard? c)
    {
        if (c is null || c.Status != WashStatus.Waiting) return;
        if (!OndaMessageBox.Ask(FindForm(), $"{c.Plate} için {c.PackageName} siparişi iptal edilsin mi?", "İptal")) return;
        var r = await App.Send(new CancelWash(c.Id));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        await ReloadAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void LayoutPage(Rectangle r)
    {
        _new.Location = new Point(r.X, r.Y);
        RightAlign(r.Right, r.Y, _washer, _cancel, _advance);
        _board.SetBounds(r.X, r.Y + 54, r.Width, r.Height - 54);
    }
}
