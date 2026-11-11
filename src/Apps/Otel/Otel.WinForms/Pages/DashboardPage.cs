using Otel.Application;

namespace Otel.WinForms.Pages;

/// <summary>OCC, ADR, RevPAR ve günlük hareketler.</summary>
sealed class DashboardPage : PageBase
{
    readonly OndaStatCard _occ = new() { Title = "Doluluk · son 30 gün", Icon = "", Prefix = "%", Decimals = 1, DeltaText = "önceki 30 güne göre" };
    readonly OndaStatCard _adr = new() { Title = "ADR · ortalama gecelik", Icon = "", Prefix = "₺", IconColor = App.Violet, DeltaText = "önceki 30 güne göre" };
    readonly OndaStatCard _revpar = new() { Title = "RevPAR · oda başına gelir", Icon = "", Prefix = "₺", IconColor = Color.FromArgb(20, 184, 166), DeltaText = "önceki 30 güne göre" };
    readonly OndaStatCard _inhouse = new() { Title = "Konaklayan misafir", Icon = "", IconColor = Color.FromArgb(245, 158, 11), DeltaText = "" };

    readonly OndaChart _trend = new() { Title = "Günlük doluluk", Subtitle = "Son 30 gün (%)", ChartType = OndaChartType.Area, ShowLegend = false };
    readonly OndaChart _types = new() { Title = "Oda tipine göre", Subtitle = "Son 30 gün oda geliri (₺)", ChartType = OndaChartType.Bar, ShowLegend = false };
    readonly OndaLabel _gridTitle = new() { Text = "Bugünkü giriş ve çıkışlar", Style = OndaLabelStyle.Title };
    readonly OndaGrid _grid = new() { ShowFilterRow = false, RowHeight = 36, EmptyText = "Bugün giriş ya da çıkış yok" };

    public DashboardPage() : base("Gösterge paneli", "Yükleniyor…")
    {
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.Movement), "Hareket", 110, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.GuestName), "Misafir", 260));
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.RoomNumber), "Oda", 90));
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.CheckIn), "Giriş", 130) { Format = "dd.MM.yyyy" });
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.CheckOut), "Çıkış", 130) { Format = "dd.MM.yyyy" });
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.StatusText), "Durum", 140, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.RowActivated += async (_, e) =>
        {
            if (e.Item is MovementRow m && await ReservationDetailForm.OpenAsync(FindForm(), m.ReservationId)) await ReloadAsync();
        };
        _types.ValueFormat = OndaChart.Compact;
        Controls.AddRange(new Control[] { _occ, _adr, _revpar, _inhouse, _trend, _types, _gridTitle, _grid });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new GetDashboard());
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var d = result.Value;

        Subtitle = $"{d.Today:dd MMMM yyyy, dddd}  ·  {d.ArrivalsToday} giriş, {d.DeparturesToday} çıkış  ·  {d.DirtyRooms} kirli, {d.MaintenanceRooms} bakımdaki oda";

        _occ.Value = d.Last30.Occupancy * 100;
        _occ.Delta = (double)((d.Last30.Occupancy - d.Previous30.Occupancy) * 100);
        _occ.Sparkline = d.OccupancyTrend.TakeLast(14).ToArray();

        _adr.Value = d.Last30.Adr;
        _adr.Delta = Change(d.Last30.Adr, d.Previous30.Adr);
        _adr.Sparkline = d.RevenueTrend.TakeLast(14).ToArray();

        _revpar.Value = d.Last30.RevPar;
        _revpar.Delta = Change(d.Last30.RevPar, d.Previous30.RevPar);
        _revpar.Sparkline = d.RevenueTrend.TakeLast(14).Select(v => v / Math.Max(1, d.RoomCount)).ToArray();

        _inhouse.Value = d.InHouseGuests;
        _inhouse.DeltaText = $"bugünkü doluluk %{d.OccupancyToday * 100:0}";

        _trend.Labels = d.TrendLabels;
        _trend.Series.Clear();
        _trend.Series.Add(new OndaSeries("Doluluk", d.OccupancyTrend));
        _types.Labels = d.RoomTypeLabels;
        _types.Series.Clear();
        _types.Series.Add(new OndaSeries("Oda geliri", d.RevenueByRoomType, Color.FromArgb(20, 184, 166)));

        _grid.DataSource = d.Movements;
        foreach (var card in new[] { _occ, _adr, _revpar, _inhouse }) card.Replay();
        _trend.Replay();
        _types.Replay();
    }

    static double? Change(decimal now, decimal before) => before == 0 ? null : (double)((now - before) / before * 100);

    protected override void LayoutPage(Rectangle r)
    {
        var cw = (r.Width - Gap * 3) / 4;
        var cards = new[] { _occ, _adr, _revpar, _inhouse };
        for (var i = 0; i < 4; i++) cards[i].SetBounds(r.X + i * (cw + Gap), r.Y, cw, 150);

        var y = r.Y + 150 + Gap;
        var chartH = Math.Max(240, Math.Min(330, (r.Bottom - y) / 2));
        var leftW = (int)((r.Width - Gap) * 0.62);
        _trend.SetBounds(r.X, y, leftW, chartH);
        _types.SetBounds(r.X + leftW + Gap, y, r.Width - leftW - Gap, chartH);

        y += chartH + Gap;
        _gridTitle.Location = new Point(r.X - 2, y + 2);
        y += 36;
        _grid.SetBounds(r.X, y, r.Width, Math.Max(140, r.Bottom - y));
    }
}
