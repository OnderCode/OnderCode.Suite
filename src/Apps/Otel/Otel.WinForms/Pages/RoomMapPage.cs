using Otel.Application;
using Otel.Domain;
using Otel.WinForms.Controls;

namespace Otel.WinForms.Pages;

/// <summary>Oda haritası ve seçili odanın hızlı işlemleri.</summary>
sealed class RoomMapPage : PageBase
{
    readonly RoomMap _map = new();
    readonly OndaCard _card = new() { Title = "Oda seçin", Icon = "" };
    readonly OndaLabel _info = new() { Style = OndaLabelStyle.Body, Text = "Haritadan bir odaya tıklayın.\nÇift tıklama ana işlemi yapar." };
    readonly OndaButton _primary = new() { Width = 190 };
    readonly OndaButton _secondary = new() { Kind = OndaButtonKind.Outline, Width = 190 };
    readonly OndaButton _tertiary = new() { Kind = OndaButtonKind.Ghost, Width = 150 };
    readonly HashSet<OndaButton> _shown = [];
    readonly OndaButton _refresh = new() { Action = OndaAction.Refresh, Kind = OndaButtonKind.Ghost, ButtonSize = OndaButtonSize.Small, Width = 110 };

    Func<Task>? _primaryAction, _secondaryAction, _tertiaryAction;

    public RoomMapPage() : base("Oda haritası", "Odaların anlık durumu; tıklayın, işlem yapın.")
    {
        _card.Controls.AddRange(new Control[] { _info, _primary, _secondary, _tertiary });
        _primary.Click += async (_, _) => await Run(_primaryAction, _primary);
        _secondary.Click += async (_, _) => await Run(_secondaryAction, _secondary);
        _tertiary.Click += async (_, _) => await Run(_tertiaryAction, _tertiary);
        _refresh.Click += async (_, _) => await ReloadAsync();
        _map.RoomSelected += (_, t) => ShowRoom(t);
        _map.RoomActivated += async (_, _) => await Run(_primaryAction, _primary);
        Controls.AddRange(new Control[] { _map, _card, _refresh });
        ShowRoom(null);
    }

    public RoomMap Map => _map;

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new GetRoomBoard());
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        _map.SetTiles(result.Value);
        ShowRoom(_map.Selected);
    }

    static async Task Run(Func<Task>? action, OndaButton button)
    {
        if (action is null) return;
        button.Busy = true;
        try { await action(); }
        finally { button.Busy = false; }
    }

    void ShowRoom(RoomTile? t)
    {
        _primaryAction = _secondaryAction = _tertiaryAction = null;
        if (t is null)
        {
            _card.Title = "Oda seçin";
            _shown.Clear();
            _primary.Visible = _secondary.Visible = _tertiary.Visible = false;
            DoCardLayout();
            return;
        }

        _card.Title = $"Oda {t.Number} · {t.StatusText}";
        var lines = new List<string> { $"{t.RoomType}  ·  {t.Floor}. kat  ·  {t.Capacity} kişilik  ·  taban fiyat {t.BaseRate:N0} ₺ / gece" };
        switch (t.Status)
        {
            case RoomStatus.Occupied:
                lines.Add($"Misafir: {t.GuestName}");
                if (t.CheckOut is { } co) lines[^1] += $"  ·  çıkış {co:dd MMMM yyyy}";
                Set(_primary, "Çıkış yap", "", OndaButtonKind.Primary, () => CheckOutAsync(t));
                Set(_secondary, "Folyo ve detay", "", null, () => OpenReservation(t.ReservationId));
                Set(_tertiary, null, null, null, null);
                break;
            case RoomStatus.Available when t.ArrivalReservationId is int arrival:
                lines.Add($"Bugün giriş: {t.ArrivalGuest}");
                Set(_primary, "Giriş yap", "", OndaButtonKind.Success, () => CheckInAsync(arrival, t));
                Set(_secondary, "Rezervasyon detayı", "", null, () => OpenReservation(arrival));
                Set(_tertiary, "Bakıma al", "", null, () => MaintenanceAsync(t));
                break;
            case RoomStatus.Available:
                lines.Add("Oda temiz ve satışa hazır. Çift tıklama: yeni rezervasyon");
                Set(_primary, "Yeni rezervasyon", "", OndaButtonKind.Primary, () => NewReservation(t));
                Set(_secondary, "Bakıma al", "", null, () => MaintenanceAsync(t));
                Set(_tertiary, null, null, null, null);
                break;
            case RoomStatus.Dirty:
                lines.Add("Çıkış yapıldı, temizlik bekliyor.");
                Set(_primary, "Temizlendi", "", OndaButtonKind.Success, () => ReadyAsync(t, "temizlendi, satışa açıldı"));
                Set(_secondary, null, null, null, null);
                Set(_tertiary, null, null, null, null);
                break;
            default:
                lines.Add($"Bakım: {t.MaintenanceNote}");
                Set(_primary, "Bakım tamamlandı", "", OndaButtonKind.Success, () => ReadyAsync(t, "bakımdan çıktı, satışa açıldı"));
                Set(_secondary, null, null, null, null);
                Set(_tertiary, null, null, null, null);
                break;
        }
        _info.Text = string.Join("\n", lines);
        DoCardLayout();
    }

    void Set(OndaButton button, string? text, string? icon, OndaButtonKind? kind, Func<Task>? action)
    {
        button.Visible = text is not null;
        if (text is null) { _shown.Remove(button); return; }
        _shown.Add(button);
        button.Text = text;
        button.Icon = icon ?? "";
        if (kind is { } k) button.Kind = k;
        if (button == _primary) _primaryAction = action;
        else if (button == _secondary) _secondaryAction = action;
        else _tertiaryAction = action;
    }

    async Task CheckInAsync(int reservationId, RoomTile t)
    {
        var r = await App.Send(new CheckIn(reservationId));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        OndaToast.Success($"{t.ArrivalGuest} odası {t.Number}'e yerleşti.", "Giriş yapıldı");
        await ReloadAsync();
    }

    async Task CheckOutAsync(RoomTile t)
    {
        if (t.ReservationId is not int id) return;
        if (!OndaMessageBox.Ask(FindForm(), $"{t.GuestName} için çıkış yapılsın mı?", "Çıkış")) return;
        var r = await App.Send(new CheckOut(id));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        var s = r.Value;
        OndaMessageBox.Success(FindForm(),
            $"{s.GuestName} · Oda {s.RoomNumber} · {s.Nights} gece\n\nKonaklama: {App.Money(s.RoomTotal)}\nHarcamalar: {App.Money(s.ChargesTotal)}\nToplam: {App.Money(s.Balance)}\n\n" +
            $"Sadakat puanı: {s.LoyaltyPoints} ({s.Tier})\nOda temizlik listesine eklendi.", "Çıkış tamamlandı");
        await ReloadAsync();
    }

    async Task MaintenanceAsync(RoomTile t)
    {
        if (!OndaMessageBox.Ask(FindForm(), $"Oda {t.Number} bakıma alınsın mı? Satışa kapanır.", "Bakım")) return;
        var r = await App.Send(new StartMaintenance(t.RoomId, "Genel bakım"));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        OndaToast.Warning($"Oda {t.Number} bakıma alındı.");
        await ReloadAsync();
    }

    async Task ReadyAsync(RoomTile t, string message)
    {
        var r = await App.Send(new MarkRoomReady(t.RoomId));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        OndaToast.Success($"Oda {t.Number} {message}.");
        await ReloadAsync();
    }

    async Task OpenReservation(int? reservationId)
    {
        if (reservationId is int id && await ReservationDetailForm.OpenAsync(FindForm(), id)) await ReloadAsync();
    }

    async Task NewReservation(RoomTile t)
    {
        if (ReservationForm.Open(FindForm(), t.RoomId, App.Today) is not null) await ReloadAsync();
    }

    void DoCardLayout()
    {
        var top = _card.ContentTop;
        var x = _card.Width - 18;
        foreach (var b in new[] { _primary, _secondary, _tertiary })
        {
            if (!_shown.Contains(b)) continue;
            x -= b.Width;
            b.Location = new Point(x, top + 8);
            x -= 10;
        }
        _info.MaximumSize = new Size(Math.Max(200, x - 30), 0);
        _info.Location = new Point(18, top + 2);
    }

    protected override void LayoutPage(Rectangle r)
    {
        const int cardH = 122;
        _refresh.Location = new Point(r.Right - _refresh.Width, r.Y - 44);
        _map.SetBounds(r.X, r.Y, r.Width, r.Height - cardH - 16);
        _card.SetBounds(r.X, r.Bottom - cardH, r.Width, cardH);
        DoCardLayout();
    }
}
