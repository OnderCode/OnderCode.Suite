using Otel.Application;
using Otel.Domain;

namespace Otel.WinForms;

/// <summary>
/// Yeni rezervasyon: tarih ve kişi sayısı değiştikçe yalnızca müsait odalar listelenir, fiyat motoru anında hesaplar.
/// Kayıtta çakışma kontrolü yeniden, transaction içinde yapılır.
/// </summary>
sealed class ReservationForm : OndaForm
{
    const int M = 28;
    readonly int? _presetRoom;

    readonly OndaSegmented _guestMode = new() { Items = ["Kayıtlı misafir", "Yeni misafir"] };
    readonly OndaLookup _guest = new() { Label = "Misafir", Required = true, DisplayMember = nameof(GuestOption.FullName), ValueMember = nameof(GuestOption.Id), PopupWidth = 470, Placeholder = "Adını yazın…", Icon = "" };
    readonly OndaTextBox _newName = new() { Label = "Ad soyad", Required = true, Icon = "" };
    readonly OndaTextBox _newId = new() { Label = "T.C. kimlik / pasaport no", HelperText = "İsteğe bağlı; 11 haneyse T.C. kimlik algoritmasıyla doğrulanır" };
    readonly OndaDatePicker _in = new() { Label = "Giriş", Required = true };
    readonly OndaDatePicker _out = new() { Label = "Çıkış", Required = true };
    readonly OndaTextBox _adults = new() { Label = "Kişi", Kind = OndaInputKind.Integer, Required = true, Text = "2" };
    readonly OndaLookup _room = new() { Label = "Oda", Required = true, DisplayMember = nameof(AvailableRoom.Display), ValueMember = nameof(AvailableRoom.RoomId), PopupWidth = 600, Placeholder = "Müsait odalar", Icon = "" };
    readonly OndaTextBox _notes = new() { Label = "Not", Placeholder = "Geç giriş, bebek yatağı…" };

    readonly OndaCard _priceCard = new() { Title = "Fiyat", Icon = "" };
    readonly OndaLabel _priceLines = new() { Style = OndaLabelStyle.Body, Text = "Tarih ve oda seçin." };
    readonly OndaLabel _priceTotal = new() { Style = OndaLabelStyle.Header, Text = "" };

    readonly OndaButton _cancel = new() { Action = OndaAction.Cancel, DialogResult = DialogResult.Cancel, Width = 120 };
    readonly OndaButton _save = new() { Action = OndaAction.Save, Text = "Rezervasyonu kaydet", Shortcut = Keys.F2, Width = 210 };

    int _loadVersion;
    bool _existingGuest = true;

    public int? CreatedId { get; private set; }

    /// <summary>Formu açar; kayıt yapılırsa yeni rezervasyonun kimliğini döner.</summary>
    public static int? Open(IWin32Window? owner, int? roomId = null, DateOnly? checkIn = null)
    {
        using var form = new ReservationForm(roomId, checkIn);
        return form.ShowDialog(owner) == DialogResult.OK ? form.CreatedId : null;
    }

    public ReservationForm(int? roomId = null, DateOnly? checkIn = null)
    {
        _presetRoom = roomId;
        Text = "Yeni rezervasyon";
        TitleIcon = "";
        ShowMinimizeButton = ShowMaximizeButton = false;
        Resizable = false;
        CloseOnEscape = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(940, 640);

        _guest.Columns.Add(new OndaGridColumn(nameof(GuestOption.FullName), "Misafir", 220));
        _guest.Columns.Add(new OndaGridColumn(nameof(GuestOption.TierText), "Seviye", 90, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _guest.Columns.Add(new OndaGridColumn(nameof(GuestOption.LoyaltyPoints), "Puan", 70, OndaColumnKind.Number));
        _guest.Columns.Add(new OndaGridColumn(nameof(GuestOption.StayCount), "Konaklama", 80, OndaColumnKind.Number));

        _room.Columns.Add(new OndaGridColumn(nameof(AvailableRoom.Number), "Oda", 60));
        _room.Columns.Add(new OndaGridColumn(nameof(AvailableRoom.RoomType), "Tip", 130));
        _room.Columns.Add(new OndaGridColumn(nameof(AvailableRoom.Capacity), "Kişi", 55, OndaColumnKind.Number));
        _room.Columns.Add(new OndaGridColumn(nameof(AvailableRoom.AverageNightly), "Ort. gecelik", 130, OndaColumnKind.Money));
        _room.Columns.Add(new OndaGridColumn(nameof(AvailableRoom.Total), "Toplam", 140, OndaColumnKind.Money));

        var start = checkIn ?? App.Today;
        _in.Value = start.ToDateTime(TimeOnly.MinValue);
        _out.Value = start.AddDays(2).ToDateTime(TimeOnly.MinValue);
        _in.MinDate = App.Today.ToDateTime(TimeOnly.MinValue);

        _priceLines.MaximumSize = new Size(280, 0);
        _priceCard.Controls.AddRange(new Control[] { _priceLines, _priceTotal });
        Controls.AddRange(new Control[] { _guestMode, _guest, _newName, _newId, _in, _out, _adults, _room, _notes, _priceCard, _cancel, _save });

        _guestMode.SelectedIndexChanged += (_, _) => ApplyGuestMode();
        _in.ValueChanged += async (_, _) =>
        {
            if (_in.Value is { } v && (_out.Value is null || _out.Value <= v)) _out.Value = v.AddDays(1);
            await LoadRoomsAsync();
        };
        _out.ValueChanged += async (_, _) => await LoadRoomsAsync();
        _adults.Leave += async (_, _) => await LoadRoomsAsync();
        _room.SelectedValueChanged += async (_, _) => await QuoteAsync();
        _save.Click += async (_, _) => await SaveAsync();
        Resize += (_, _) => DoLayout();
        ApplyGuestMode();
        DoLayout();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var guests = await App.Send(new GetGuestOptions());
        if (guests.IsSuccess) _guest.DataSource = guests.Value;
        await LoadRoomsAsync();
        _guest.FocusInput();
    }

    void ApplyGuestMode()
    {
        var existing = _existingGuest = _guestMode.SelectedIndex <= 0;
        _guest.Visible = _guest.Enabled = existing;
        _newName.Visible = _newName.Enabled = !existing;
        _newId.Visible = _newId.Enabled = !existing;
        DoLayout();
    }

    DateOnly? CheckInDate => _in.Value is { } d ? DateOnly.FromDateTime(d) : null;
    DateOnly? CheckOutDate => _out.Value is { } d ? DateOnly.FromDateTime(d) : null;
    int Adults => int.TryParse(_adults.Text, out var a) ? a : 0;

    public async Task LoadRoomsAsync()
    {
        if (CheckInDate is not { } ci || CheckOutDate is not { } co || co <= ci || Adults < 1) return;
        var version = ++_loadVersion;
        var keep = _room.SelectedValue as int? ?? _presetRoom;
        var result = await App.Send(new FindAvailableRooms(ci, co, Adults));
        if (version != _loadVersion) return; // daha yeni bir istek var
        if (!result.IsSuccess) { App.ShowErrors(result); return; }

        _room.DataSource = result.Value;
        _room.HelperText = result.Value.Count == 0 ? "Bu tarihlerde müsait oda yok" : $"{result.Value.Count} müsait oda";
        _room.SelectedValue = keep is int id && result.Value.Any(r => r.RoomId == id) ? id : null;
        await QuoteAsync();
    }

    async Task QuoteAsync()
    {
        if (_room.SelectedValue is not int roomId || CheckInDate is not { } ci || CheckOutDate is not { } co)
        {
            _priceLines.Text = "Tarih ve oda seçin.";
            _priceTotal.Text = "";
            DoPriceLayout();
            return;
        }
        var result = await App.Send(new QuoteStay(roomId, ci, co));
        if (!result.IsSuccess) return;
        var q = result.Value;
        var policy = PricingPolicy.Default;
        var lines = new List<string> { $"{q.NightCount} gece · ortalama {App.Money(q.AverageNightly)}" };
        var weekend = q.Nights.Count(n => n.IsWeekend);
        if (weekend > 0) lines.Add($"{weekend} hafta sonu gecesi (×{policy.WeekendMultiplier:0.##})");
        foreach (var s in q.Nights.Where(n => n.SeasonName is not null).GroupBy(n => n.SeasonName))
            lines.Add($"{s.Count()} gece {s.Key} sezonu (×{s.First().SeasonMultiplier:0.##})");
        lines.Add($"Ara toplam: {App.Money(q.Subtotal)}");
        if (q.EarlyBookingDiscount > 0) lines.Add($"Erken rezervasyon (%{policy.EarlyBookingRate * 100:0}): −{App.Money(q.EarlyBookingDiscount)}");
        else lines.Add($"{policy.EarlyBookingDays} gün önceden alınan rezervasyona %{policy.EarlyBookingRate * 100:0} indirim");
        _priceLines.Text = string.Join("\n", lines);
        _priceTotal.Text = App.Money(q.Total);
        DoPriceLayout();
    }

    async Task SaveAsync()
    {
        foreach (var c in new OndaInputBase[] { _guest, _newName, _newId, _in, _out, _adults, _room }) c.SetError(null);
        if (!OndaValidate.All(this, out var bad))
        {
            bad?.FocusInput();
            OndaToast.Error("Kırmızı işaretli alanları düzeltin.", "Kaydedilemedi");
            return;
        }
        var existing = _guestMode.SelectedIndex <= 0;
        var command = new CreateReservation(
            existing ? _guest.SelectedValue as int? : null,
            existing ? null : _newName.Text,
            existing ? null : _newId.Text,
            _room.SelectedValue as int? ?? 0,
            CheckInDate ?? App.Today, CheckOutDate ?? App.Today, Adults, _notes.Text);

        _save.Busy = true;
        var result = await App.Send(command);
        _save.Busy = false;
        if (!result.IsSuccess)
        {
            foreach (var error in result.Errors)
            {
                OndaInputBase? target = error.Field switch
                {
                    nameof(CreateReservation.GuestId) or nameof(CreateReservation.NewGuestName) => existing ? _guest : _newName,
                    nameof(CreateReservation.NewGuestIdentityNo) => _newId,
                    nameof(CreateReservation.CheckIn) => _in,
                    nameof(CreateReservation.CheckOut) => _out,
                    nameof(CreateReservation.Adults) => _adults,
                    nameof(CreateReservation.RoomId) => _room,
                    _ => null,
                };
                target?.SetError(error.Message);
            }
            App.ShowErrors(result, "Rezervasyon kaydedilemedi");
            if (result.Errors.Any(e => e.Code == OnderCode.Application.Results.ErrorCodes.Domain)) await LoadRoomsAsync();
            return;
        }
        CreatedId = result.Value;
        OndaToast.Success("Rezervasyon kaydedildi.", "Tamam");
        DialogResult = DialogResult.OK;
    }

    /// <summary>Ekran görüntüsü için formu örnek değerlerle doldurur.</summary>
    public async Task FillSampleAsync()
    {
        if (_guest.DataSource is IReadOnlyList<GuestOption> { Count: > 3 } guests) _guest.SelectedValue = guests[3].Id;
        await LoadRoomsAsync();
        if (_room.DataSource is IReadOnlyList<AvailableRoom> { Count: > 0 } rooms)
            _room.SelectedValue = rooms.FirstOrDefault(r => r.RoomType != rooms[0].RoomType)?.RoomId ?? rooms[0].RoomId;
        await QuoteAsync();
    }

    void DoLayout()
    {
        const int cardW = 320;
        var top = CaptionHeight + 22;
        var formW = ClientSize.Width - M * 3 - cardW;
        _guestMode.SetBounds(M, top, 320, 38);
        var y = top + 54;
        if (_existingGuest)
        {
            _guest.SetBounds(M, y, formW, _guest.Height);
            y = _guest.Bottom + 6;
        }
        else
        {
            var half = (formW - 16) / 2;
            _newName.SetBounds(M, y, half, _newName.Height);
            _newId.SetBounds(M + half + 16, y, half, _newId.Height);
            y = _newName.Bottom + 6;
        }
        var third = (formW - 32) / 3;
        _in.SetBounds(M, y, third, _in.Height);
        _out.SetBounds(M + third + 16, y, third, _out.Height);
        _adults.SetBounds(M + (third + 16) * 2, y, third, _adults.Height);
        y = _in.Bottom + 6;
        _room.SetBounds(M, y, formW, _room.Height);
        y = _room.Bottom + 6;
        _notes.SetBounds(M, y, formW, _notes.Height);

        _priceCard.SetBounds(ClientSize.Width - M - cardW, top + 54, cardW, 330);
        DoPriceLayout();

        var by = ClientSize.Height - 36 - 22;
        _save.Location = new Point(ClientSize.Width - M - _save.Width, by);
        _cancel.Location = new Point(_save.Left - 10 - _cancel.Width, by);
    }

    void DoPriceLayout()
    {
        _priceLines.Location = new Point(18, _priceCard.ContentTop + 4);
        _priceTotal.Location = new Point(16, _priceCard.Height - 58);
    }
}
