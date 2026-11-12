using OnderCode.Kernel.Validation;
using Otopark.Application;
using Otopark.Domain;

namespace Otopark.WinForms;

/// <summary>Çıkış ve tahsilat: süre, tarife dökümü, bağlı yıkama siparişleri ve toplam. Yıkaması bitmemiş araç çıkamaz.</summary>
sealed class ExitForm : OndaForm
{
    const int M = 28;
    readonly int _sessionId;
    ExitQuote? _quote;

    readonly OndaLabel _plate = new() { Style = OndaLabelStyle.Header };
    readonly OndaLabel _sub = new() { Style = OndaLabelStyle.Muted };
    readonly OndaCard _feeCard = new() { Title = "Otopark ücreti", Icon = "" };
    readonly OndaLabel _feeLines = new() { Style = OndaLabelStyle.Body };
    readonly OndaCard _washCard = new() { Title = "Yıkama", Icon = "" };
    readonly OndaGrid _washes = new() { ShowFilterRow = false, RowHeight = 32, EmptyText = "Yıkama yok" };
    readonly OndaLabel _warning = new() { Style = OndaLabelStyle.Strong };
    readonly OndaLabel _totalCaption = new() { Style = OndaLabelStyle.Muted, Text = "Tahsil edilecek" };
    readonly OndaLabel _total = new() { Style = OndaLabelStyle.Header };
    readonly OndaSegmented _payment = new() { Items = ["Kart", "Nakit"] };

    readonly OndaButton _close = new() { Action = OndaAction.Cancel, DialogResult = DialogResult.Cancel, Width = 120 };
    readonly OndaButton _pay = new() { Text = "Tahsil et ve çıkış", Icon = "", Kind = OndaButtonKind.Success, Shortcut = Keys.F2, Width = 220 };

    public ExitReceipt? Receipt { get; private set; }

    public static async Task<bool> OpenAsync(IWin32Window? owner, int sessionId)
    {
        using var form = new ExitForm(sessionId);
        if (!await form.LoadAsync()) return false;
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    /// <summary>Plaka sorar, içerideki aracı bulup çıkış formunu açar (F3).</summary>
    public static async Task<bool> OpenByPlateAsync(IWin32Window? owner)
    {
        using var prompt = new PlatePrompt();
        if (prompt.ShowDialog(owner) != DialogResult.OK) return false;
        var found = await App.Send(new FindActiveSession(prompt.Plate));
        if (!found.IsSuccess) { App.ShowErrors(found); return false; }
        if (found.Value is not int id)
        {
            OndaToast.Warning($"{TurkishPlate.Normalize(prompt.Plate) ?? prompt.Plate} içeride görünmüyor.", "Araç bulunamadı");
            return false;
        }
        return await OpenAsync(owner, id);
    }

    public ExitForm(int sessionId)
    {
        _sessionId = sessionId;
        Text = "Çıkış ve tahsilat";
        TitleIcon = "";
        ShowMinimizeButton = ShowMaximizeButton = false;
        Resizable = false;
        CloseOnEscape = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(860, 600);

        _payment.SelectedIndex = 0;
        _washes.Columns.Add(new OndaGridColumn(nameof(WashLine.PackageName), "Paket", 150));
        _washes.Columns.Add(new OndaGridColumn(nameof(WashLine.StatusText), "Durum", 96, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _washes.Columns.Add(new OndaGridColumn(nameof(WashLine.Price), "Tutar", 92, OndaColumnKind.Money));
        _feeLines.MaximumSize = new Size(340, 0);
        _feeCard.Controls.Add(_feeLines);
        _washCard.Controls.Add(_washes);
        Controls.AddRange(new Control[] { _plate, _sub, _feeCard, _washCard, _warning, _totalCaption, _total, _payment, _close, _pay });
        _pay.Click += async (_, _) => await PayAsync();
        DoLayout();
    }

    public async Task<bool> LoadAsync()
    {
        var result = await App.Send(new QuoteExit(_sessionId));
        if (!result.IsSuccess) { App.ShowErrors(result); return false; }
        var q = _quote = result.Value;
        _plate.Text = q.Plate;
        _sub.Text = $"{q.VehicleText}  ·  {q.SpotCode}  ·  giriş {q.EntryAt:dd.MM HH:mm}  ·  {q.DurationText}" + (q.IsSubscriber ? $"  ·  abone: {q.SubscriberName}" : "");

        var f = q.Fee;
        var lines = new List<string> { f.Explanation, "", $"Tarife tutarı: {App.Money(f.Gross)}" };
        if (f.WashDiscount > 0) lines.Add($"Yıkama indirimi: −{App.Money(f.WashDiscount)}");
        if (f.SubscriptionDiscount > 0) lines.Add($"Abonelik: −{App.Money(f.SubscriptionDiscount)}");
        lines.Add($"Otopark: {App.Money(f.Total)}");
        _feeLines.Text = string.Join("\n", lines);

        _washes.DataSource = q.Washes;
        _warning.Text = q.BlockReason ?? "";
        _warning.ForeColor = OndaTheme.Colors.Danger;
        _total.Text = App.Money(q.GrandTotal);
        _pay.Enabled = q.BlockReason is null;
        _payment.Visible = q.GrandTotal > 0;
        DoLayout();
        return true;
    }

    async Task PayAsync()
    {
        if (_quote is null || _quote.BlockReason is not null) return;
        _pay.Busy = true;
        var payment = _payment.SelectedIndex == 1 ? PaymentMethod.Cash : PaymentMethod.Card;
        var result = await App.Send(new RegisterExit(_sessionId, payment));
        _pay.Busy = false;
        if (!result.IsSuccess) { App.ShowErrors(result, "Çıkış yapılamadı"); await LoadAsync(); return; }
        Receipt = result.Value;
        var r = result.Value;
        OndaToast.Success($"{r.Plate} · {r.DurationText} · {App.Money(r.Total)} ({r.PaymentText})", "Çıkış yapıldı");
        DialogResult = DialogResult.OK;
    }

    void DoLayout()
    {
        var top = CaptionHeight + 18;
        _plate.Location = new Point(M - 3, top);
        _sub.Location = new Point(M - 1, top + 40);
        var cardTop = top + 76;
        var half = (ClientSize.Width - M * 2 - 20) / 2;
        _feeCard.SetBounds(M, cardTop, half, 250);
        _feeLines.Location = new Point(18, _feeCard.ContentTop + 4);
        _washCard.SetBounds(M + half + 20, cardTop, half, 250);
        _washes.SetBounds(16, _washCard.ContentTop, _washCard.Width - 32, _washCard.Height - _washCard.ContentTop - 16);

        var y = cardTop + 266;
        _warning.Location = new Point(M, y);
        _totalCaption.Location = new Point(M, y + 30);
        _total.Location = new Point(M - 3, y + 50);
        _payment.SetBounds(ClientSize.Width - M - 260, y + 40, 260, 38);

        var by = ClientSize.Height - 36 - 22;
        _pay.Location = new Point(ClientSize.Width - M - _pay.Width, by);
        _close.Location = new Point(_pay.Left - 10 - _close.Width, by);
    }

    /// <summary>Gişede plaka sorma penceresi.</summary>
    sealed class PlatePrompt : OndaForm
    {
        readonly OndaTextBox _text = new() { Label = "Çıkış yapacak aracın plakası", Required = true, Placeholder = "34 XWQ 123", Icon = "", MaxLength = 12, EnterMovesNext = false };
        readonly OndaButton _ok = new() { Text = "Bul", Icon = "", Width = 120 };
        readonly OndaButton _cancel = new() { Action = OndaAction.Cancel, DialogResult = DialogResult.Cancel, Width = 110 };

        public string Plate => _text.Text;

        public PlatePrompt()
        {
            Text = "Araç çıkışı";
            TitleIcon = "";
            ShowMinimizeButton = ShowMaximizeButton = false;
            Resizable = false;
            CloseOnEscape = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(460, 250);
            Controls.AddRange(new Control[] { _text, _ok, _cancel });
            _ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_text.Text)) DialogResult = DialogResult.OK; };
            _text.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && !string.IsNullOrWhiteSpace(_text.Text)) { e.Handled = true; DialogResult = DialogResult.OK; } };
            var top = CaptionHeight + 20;
            _text.SetBounds(24, top, ClientSize.Width - 48, _text.Height);
            var by = ClientSize.Height - 36 - 20;
            _ok.Location = new Point(ClientSize.Width - 24 - _ok.Width, by);
            _cancel.Location = new Point(_ok.Left - 10 - _cancel.Width, by);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _text.FocusInput();
        }
    }
}
