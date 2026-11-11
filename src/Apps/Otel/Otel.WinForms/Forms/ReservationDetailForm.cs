using Otel.Application;
using Otel.Domain;

namespace Otel.WinForms;

/// <summary>Rezervasyon detayı ve folyo: giriş, çıkış, iptal, oda hesabına harcama.</summary>
sealed class ReservationDetailForm : OndaForm
{
    const int M = 28;
    static readonly ChargeCategory[] Categories = Enum.GetValues<ChargeCategory>();
    readonly int _id;
    ReservationDetail? _detail;

    readonly OndaLabel _code = new() { Style = OndaLabelStyle.Header };
    readonly OndaLabel _status = new() { Style = OndaLabelStyle.Strong };
    readonly OndaCard _infoCard = new() { Title = "Konaklama", Icon = "" };
    readonly OndaLabel _info = new() { Style = OndaLabelStyle.Body };
    readonly OndaCard _folioCard = new() { Title = "Folyo", Icon = "" };
    readonly OndaGrid _charges = new() { ShowFilterRow = false, RowHeight = 32, EmptyText = "Harcama yok" };
    readonly OndaLabel _totals = new() { Style = OndaLabelStyle.Strong };
    readonly OndaComboBox _category = new() { Label = "Tür", Compact = true };
    readonly OndaTextBox _description = new() { Label = "Açıklama", Compact = true, Placeholder = "Minibar, akşam yemeği…" };
    readonly OndaTextBox _amount = new() { Label = "Tutar", Kind = OndaInputKind.Money, Compact = true };
    readonly OndaButton _addCharge = new() { Text = "Odaya yaz", Icon = "", Kind = OndaButtonKind.Soft, Width = 130 };

    readonly OndaButton _checkIn = new() { Text = "Giriş yap", Icon = "", Kind = OndaButtonKind.Success, Width = 140 };
    readonly OndaButton _checkOut = new() { Text = "Çıkış yap", Icon = "", Kind = OndaButtonKind.Primary, Width = 140 };
    readonly OndaButton _cancelRes = new() { Text = "Rezervasyonu iptal et", Icon = "", Kind = OndaButtonKind.Ghost, Width = 210 };
    readonly OndaButton _close = new() { Action = OndaAction.Close, Kind = OndaButtonKind.Outline, DialogResult = DialogResult.Cancel, Width = 120 };

    readonly HashSet<OndaButton> _shown = [];

    public bool Changed { get; private set; }

    /// <summary>Detayı açar; bir değişiklik yapıldıysa true döner.</summary>
    public static async Task<bool> OpenAsync(IWin32Window? owner, int reservationId)
    {
        using var form = new ReservationDetailForm(reservationId);
        if (!await form.LoadAsync()) return false;
        form.ShowDialog(owner);
        return form.Changed;
    }

    public ReservationDetailForm(int reservationId)
    {
        _id = reservationId;
        Text = "Rezervasyon";
        TitleIcon = "";
        ShowMinimizeButton = ShowMaximizeButton = false;
        Resizable = false;
        CloseOnEscape = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 660);

        foreach (var c in Categories) _category.Items.Add(Texts.Of(c));
        _category.SelectedItem = Texts.Of(ChargeCategory.Minibar);
        _charges.Columns.Add(new OndaGridColumn(nameof(ChargeRow.PostedAt), "Tarih", 100, OndaColumnKind.Date) { Format = "dd.MM HH:mm" });
        _charges.Columns.Add(new OndaGridColumn(nameof(ChargeRow.CategoryText), "Tür", 90));
        _charges.Columns.Add(new OndaGridColumn(nameof(ChargeRow.Description), "Açıklama", 170));
        _charges.Columns.Add(new OndaGridColumn(nameof(ChargeRow.Amount), "Tutar", 110, OndaColumnKind.Money) { ShowTotal = true });

        _info.MaximumSize = new Size(300, 0);
        _infoCard.Controls.Add(_info);
        _folioCard.Controls.AddRange(new Control[] { _charges, _totals, _category, _description, _amount, _addCharge });
        Controls.AddRange(new Control[] { _code, _status, _infoCard, _folioCard, _checkIn, _checkOut, _cancelRes, _close });

        _checkIn.Click += async (_, _) => await Do(new CheckIn(_id), _checkIn, "Giriş yapıldı, oda dolu olarak işaretlendi.");
        _cancelRes.Click += async (_, _) =>
        {
            if (OndaMessageBox.Ask(this, "Rezervasyon iptal edilsin mi?", "İptal")) await Do(new CancelReservation(_id), _cancelRes, "Rezervasyon iptal edildi.");
        };
        _checkOut.Click += async (_, _) => await CheckOutAsync();
        _addCharge.Click += async (_, _) => await AddChargeAsync();
        Resize += (_, _) => DoLayout();
        DoLayout();
    }

    public async Task<bool> LoadAsync()
    {
        var result = await App.Send(new GetReservationDetail(_id));
        if (!result.IsSuccess) { App.ShowErrors(result); return false; }
        var d = _detail = result.Value;
        Text = $"Rezervasyon · {d.Code}";
        _code.Text = $"{d.GuestName}";
        _status.Text = $"{d.Code}  ·  {d.StatusText}";
        _status.ForeColor = App.ColorOf(d.Status);
        _info.Text = string.Join("\n", new[]
        {
            $"Oda {d.RoomNumber} · {d.RoomType}",
            $"{d.CheckIn:dd MMMM yyyy} → {d.CheckOut:dd MMMM yyyy}",
            $"{d.Nights} gece · {d.Adults} kişi",
            "",
            $"Misafir seviyesi: {d.GuestTier} ({d.GuestPoints} puan)",
            $"Rezervasyon tarihi: {d.BookedOn:dd.MM.yyyy}",
            d.Notes is null ? "" : $"\nNot: {d.Notes}",
        }).TrimEnd();
        _charges.DataSource = d.Charges;
        _totals.Text = $"Konaklama {App.Money(d.RoomTotal)}   +   Harcamalar {App.Money(d.ChargesTotal)}   =   {App.Money(d.Balance)}";

        var today = App.Today;
        var inHouse = d.Status == ReservationStatus.CheckedIn;
        SetButton(_checkIn, d.Status == ReservationStatus.Confirmed && d.CheckIn <= today && today < d.CheckOut);
        SetButton(_checkOut, inHouse);
        SetButton(_cancelRes, d.Status == ReservationStatus.Confirmed);
        _category.Enabled = _description.Enabled = _amount.Enabled = _addCharge.Enabled = inHouse;
        _folioCard.Title = inHouse ? "Folyo · odaya harcama yazılabilir" : "Folyo";
        DoLayout();
        return true;
    }

    void SetButton(OndaButton button, bool visible)
    {
        button.Visible = visible;
        if (visible) _shown.Add(button); else _shown.Remove(button);
    }

    async Task Do(OnderCode.Application.Messaging.IRequest<OnderCode.Application.Results.Result> command, OndaButton button, string message)
    {
        button.Busy = true;
        var result = await App.Send(command);
        button.Busy = false;
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        Changed = true;
        OndaToast.Success(message);
        await LoadAsync();
    }

    async Task CheckOutAsync()
    {
        if (_detail is null || !OndaMessageBox.Ask(this, $"{_detail.GuestName} için çıkış yapılsın mı?\nToplam: {App.Money(_detail.Balance)}", "Çıkış")) return;
        _checkOut.Busy = true;
        var result = await App.Send(new CheckOut(_id));
        _checkOut.Busy = false;
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        Changed = true;
        var s = result.Value;
        OndaToast.Success($"Tahsil edilecek: {App.Money(s.Balance)} · {s.GuestName} {s.LoyaltyPoints} puana ulaştı ({s.Tier}).", "Çıkış tamamlandı");
        await LoadAsync();
    }

    async Task AddChargeAsync()
    {
        _description.SetError(null);
        _amount.SetError(null);
        var index = Array.IndexOf(_category.Items.Cast<string>().ToArray(), _category.SelectedItem as string);
        var command = new PostCharge(_id, Categories[Math.Max(0, index)], _description.Text, _amount.Number ?? 0);
        var result = await App.Send(command);
        if (!result.IsSuccess)
        {
            foreach (var e in result.Errors)
            {
                if (e.Field == nameof(PostCharge.Description)) _description.SetError(e.Message);
                else if (e.Field == nameof(PostCharge.Amount)) _amount.SetError(e.Message);
            }
            App.ShowErrors(result);
            return;
        }
        Changed = true;
        _description.Text = "";
        _amount.Text = "";
        await LoadAsync();
    }

    void DoLayout()
    {
        var top = CaptionHeight + 18;
        _code.Location = new Point(M - 3, top);
        _status.Location = new Point(M, top + 40);

        var cardsTop = top + 80;
        var bottom = ClientSize.Height - 36 - 22 - 16;
        const int infoW = 330;
        _infoCard.SetBounds(M, cardsTop, infoW, bottom - cardsTop);
        _info.Location = new Point(18, _infoCard.ContentTop + 4);

        var fx = M + infoW + 16;
        var fw = ClientSize.Width - fx - M;
        _folioCard.SetBounds(fx, cardsTop, fw, bottom - cardsTop);
        var inner = fw - 36;
        var y = _folioCard.ContentTop + 4;
        var inputW = (inner - 130 - 30) / 3;
        _category.SetBounds(18, y, inputW, _category.Height);
        _description.SetBounds(18 + inputW + 10, y, inputW, _description.Height);
        _amount.SetBounds(18 + (inputW + 10) * 2, y, inputW, _amount.Height);
        _addCharge.Location = new Point(18 + inner - _addCharge.Width, _amount.Bottom - _addCharge.Height - 2);
        y = _amount.Bottom + 12;
        var gridH = _folioCard.Height - y - 50;
        _charges.SetBounds(18, y, inner, Math.Max(80, gridH));
        _totals.Location = new Point(18, _charges.Bottom + 14);

        var by = ClientSize.Height - 36 - 22;
        _close.Location = new Point(ClientSize.Width - M - _close.Width, by);
        var x = _close.Left - 10;
        foreach (var b in new[] { _checkOut, _checkIn })
        {
            if (!_shown.Contains(b)) continue;
            x -= b.Width;
            b.Location = new Point(x, by);
            x -= 10;
        }
        _cancelRes.Location = new Point(M, by);
    }
}
