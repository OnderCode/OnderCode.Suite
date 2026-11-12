using Otopark.Application;
using Otopark.Domain;
using Otopark.WinForms.Controls;

namespace Otopark.WinForms.Pages;

/// <summary>Otopark haritası ve seçili park yerinin işlemleri. Süre ve ücretler her dakika tazelenir.</summary>
sealed class LotPage : PageBase
{
    readonly LotMap _map = new();
    readonly OndaCard _card = new() { Title = "Park yeri seçin", Icon = "" };
    readonly OndaLabel _info = new() { Style = OndaLabelStyle.Body, Text = "Haritadan bir yere tıklayın. Çift tıklama ana işlemi yapar." };
    readonly OndaButton _primary = new() { Width = 200 };
    readonly OndaButton _secondary = new() { Kind = OndaButtonKind.Outline, Width = 170 };
    readonly OndaButton _tertiary = new() { Kind = OndaButtonKind.Ghost, Width = 170 };
    readonly HashSet<OndaButton> _shown = [];
    readonly OndaButton _entry = new() { Text = "Araç girişi", Icon = "", Shortcut = Keys.F2, Width = 170 };
    readonly OndaButton _exit = new() { Text = "Plakayla çıkış", Icon = "", Kind = OndaButtonKind.Outline, Shortcut = Keys.F3, Width = 180 };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 60_000 };

    Func<Task>? _primaryAction, _secondaryAction, _tertiaryAction;

    public LotPage() : base("Otopark haritası", "Park yerlerinin anlık durumu")
    {
        _card.Controls.AddRange(new Control[] { _info, _primary, _secondary, _tertiary });
        _primary.Click += async (_, _) => await Run(_primaryAction, _primary);
        _secondary.Click += async (_, _) => await Run(_secondaryAction, _secondary);
        _tertiary.Click += async (_, _) => await Run(_tertiaryAction, _tertiary);
        _entry.Click += async (_, _) => { if (EntryForm.Open(FindForm()) is not null) await ReloadAsync(); };
        _exit.Click += async (_, _) => { if (await ExitForm.OpenByPlateAsync(FindForm())) await ReloadAsync(); };
        _map.SpotSelected += (_, t) => ShowSpot(t);
        _map.SpotActivated += async (_, _) => await Run(_primaryAction, _primary);
        _timer.Tick += async (_, _) => { if (Visible) await ReloadAsync(); };
        _timer.Start();
        Controls.AddRange(new Control[] { _map, _card, _entry, _exit });
        ShowSpot(null);
    }

    public LotMap Map => _map;

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new GetLotBoard());
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        _map.SetTiles(result.Value);
        var inside = result.Value.Count(t => t.Occupied);
        var usable = result.Value.Count(t => !t.OutOfService);
        Subtitle = $"{inside} / {usable} yer dolu  ·  {App.Now:HH:mm} itibarıyla  ·  süre ve ücretler dakikada bir tazelenir";
        ShowSpot(_map.Selected);
    }

    static async Task Run(Func<Task>? action, OndaButton button)
    {
        if (action is null) return;
        button.Busy = true;
        try { await action(); }
        finally { button.Busy = false; }
    }

    void ShowSpot(SpotTile? t)
    {
        _primaryAction = _secondaryAction = _tertiaryAction = null;
        if (t is null)
        {
            _card.Title = "Park yeri seçin";
            _shown.Clear();
            _primary.Visible = _secondary.Visible = _tertiary.Visible = false;
            DoCardLayout();
            return;
        }

        var lines = new List<string>();
        if (t.OutOfService)
        {
            _card.Title = $"{t.Code} · kullanım dışı";
            lines.Add($"{t.KindText} yer  ·  {t.ServiceNote}");
            Set(_primary, "Kullanıma aç", "", OndaButtonKind.Success, () => ServiceAsync(t, false));
            Set(_secondary, null, null, null, null);
            Set(_tertiary, null, null, null, null);
        }
        else if (!t.Occupied)
        {
            _card.Title = $"{t.Code} · boş";
            lines.Add($"{t.KindText} yer. Çift tıklama: bu yere araç girişi");
            var vehicle = t.Kind switch { SpotKind.Large => VehicleClass.Large, SpotKind.Motorcycle => VehicleClass.Motorcycle, _ => VehicleClass.Car };
            Set(_primary, "Bu yere giriş", "", OndaButtonKind.Primary, async () => { if (EntryForm.Open(FindForm(), t.SpotId, vehicle) is not null) await ReloadAsync(); });
            Set(_secondary, "Kullanım dışı bırak", "", null, () => ServiceAsync(t, true));
            Set(_tertiary, null, null, null, null);
        }
        else
        {
            var elapsed = t.EntryAt is { } e ? Texts.Duration(App.Now - e) : "";
            _card.Title = $"{t.Code} · {t.Plate}";
            lines.Add($"{(t.Vehicle is { } v ? Texts.Of(v) : "")}  ·  giriş {t.EntryAt:HH:mm}  ·  {elapsed}" +
                      (t.IsSubscriber ? $"  ·  abone: {t.SubscriberName}" : $"  ·  şu anki ücret {t.CurrentFee:N0} ₺"));
            if (t.Wash is { } w) lines.Add($"Yıkama: {Texts.Of(w).ToLower(Texts.Turkish)}");
            Set(_primary, "Çıkış ve tahsilat", "", OndaButtonKind.Primary, async () =>
            {
                if (t.SessionId is int id && await ExitForm.OpenAsync(FindForm(), id)) await ReloadAsync();
            });
            Set(_secondary, t.Wash is null ? "Yıkama ekle" : null, "", null, async () =>
            {
                if (WashOrderForm.Open(FindForm(), t.Plate, t.Vehicle ?? VehicleClass.Car) is not null) await ReloadAsync();
            });
            Set(_tertiary, null, null, null, null);
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

    async Task ServiceAsync(SpotTile t, bool outOfService)
    {
        if (outOfService && !OndaMessageBox.Ask(FindForm(), $"{t.Code} kullanım dışı bırakılsın mı?", "Park yeri")) return;
        var r = await App.Send(new SetSpotService(t.SpotId, outOfService, outOfService ? "Bakım" : null));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        await ReloadAsync();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void LayoutPage(Rectangle r)
    {
        const int cardH = 116;
        RightAlign(r.Right, r.Y - 50, _exit, _entry);
        _map.SetBounds(r.X, r.Y, r.Width, r.Height - cardH - 16);
        _card.SetBounds(r.X, r.Bottom - cardH, r.Width, cardH);
        DoCardLayout();
    }
}
