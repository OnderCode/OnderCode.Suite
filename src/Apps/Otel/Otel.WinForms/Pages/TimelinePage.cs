using Otel.Application;
using Otel.WinForms.Controls;

namespace Otel.WinForms.Pages;

/// <summary>Oda × gün çizelgesi. Çubuğa tıklama detay açar, boş hücreye çift tıklama o oda ve gün için rezervasyon başlatır.</summary>
sealed class TimelinePage : PageBase
{
    const int Days = 21;
    readonly ReservationTimeline _timeline = new();
    readonly OndaButton _prev = new() { Text = "Önceki", Icon = "", Kind = OndaButtonKind.Outline, ButtonSize = OndaButtonSize.Small, Width = 110 };
    readonly OndaButton _today = new() { Text = "Bugün", Kind = OndaButtonKind.Soft, ButtonSize = OndaButtonSize.Small, Width = 90 };
    readonly OndaButton _next = new() { Text = "Sonraki", Icon = "", Kind = OndaButtonKind.Outline, ButtonSize = OndaButtonSize.Small, Width = 110 };
    readonly OndaButton _new = new() { Action = OndaAction.New, Text = "Yeni rezervasyon", ButtonSize = OndaButtonSize.Small, Width = 170 };
    readonly OndaLabel _legend = new() { Style = OndaLabelStyle.Muted, Text = "Mor: onaylı  ·  Mavi: konaklıyor  ·  Gri: çıkış yaptı" };
    DateOnly _from;

    public TimelinePage() : base("Rezervasyon çizelgesi", "Üç haftalık oda doluluğu")
    {
        _from = App.Today.AddDays(-3);
        _prev.Click += async (_, _) => { _from = _from.AddDays(-7); await ReloadAsync(); };
        _next.Click += async (_, _) => { _from = _from.AddDays(7); await ReloadAsync(); };
        _today.Click += async (_, _) => { _from = App.Today.AddDays(-3); await ReloadAsync(); };
        _new.Click += async (_, _) => { if (ReservationForm.Open(FindForm()) is not null) await ReloadAsync(); };
        _timeline.ReservationClicked += async (_, id) => { if (await ReservationDetailForm.OpenAsync(FindForm(), id)) await ReloadAsync(); };
        _timeline.EmptyCellDoubleClicked += async (_, cell) =>
        {
            var start = cell.Date < App.Today ? App.Today : cell.Date;
            if (ReservationForm.Open(FindForm(), cell.RoomId, start) is not null) await ReloadAsync();
        };
        Controls.AddRange(new Control[] { _timeline, _prev, _today, _next, _new, _legend });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new GetTimeline(_from, Days));
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        _timeline.SetView(result.Value);
        Subtitle = $"{_from:dd MMMM} – {_from.AddDays(Days - 1):dd MMMM yyyy}  ·  çubuğa tıklayın: detay, boş güne çift tıklayın: yeni rezervasyon";
    }

    protected override void LayoutPage(Rectangle r)
    {
        _prev.Location = new Point(r.X, r.Y);
        _today.Location = new Point(_prev.Right + 8, r.Y);
        _next.Location = new Point(_today.Right + 8, r.Y);
        _legend.Location = new Point(_next.Right + 20, r.Y + 8);
        _new.Location = new Point(r.Right - _new.Width, r.Y);
        _timeline.SetBounds(r.X, r.Y + 48, r.Width, r.Height - 48);
    }
}
