using Otopark.Application;

namespace Otopark.WinForms.Pages;

/// <summary>Anlık doluluk, bugünkü ciro, yıkama kuyruğu, 30 günlük gelir ve saatlik giriş.</summary>
sealed class DashboardPage : PageBase
{
    readonly OndaStatCard _occ = new() { Title = "Doluluk · şu an", Icon = "", Prefix = "%", Decimals = 0 };
    readonly OndaStatCard _revenue = new() { Title = "Bugünkü ciro", Icon = "", Prefix = "₺", IconColor = App.Violet, DeltaText = "dün aynı saate göre" };
    readonly OndaStatCard _wash = new() { Title = "Yıkama kuyruğu", Icon = "", IconColor = App.Cyan };
    readonly OndaStatCard _stay = new() { Title = "Ortalama kalış · son 7 gün", Icon = "", Suffix = " dk", IconColor = Color.FromArgb(245, 158, 11) };

    readonly OndaChart _trend = new() { Title = "Son 30 gün gelir", Subtitle = "Otopark ve yıkama (₺)", ChartType = OndaChartType.Bar };
    readonly OndaChart _hours = new() { Title = "Saat saat giriş", Subtitle = "Bugün ve son 4 haftanın aynı günü", ChartType = OndaChartType.Bar };
    readonly OndaLabel _gridTitle = new() { Text = "Bugünkü hareketler", Style = OndaLabelStyle.Title };
    readonly OndaGrid _grid = new() { ShowFilterRow = false, RowHeight = 34, EmptyText = "Bugün hareket yok" };

    public DashboardPage() : base("Gösterge paneli", "Yükleniyor…")
    {
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.At), "Saat", 90, OndaColumnKind.Date) { Format = "HH:mm" });
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.Movement), "Hareket", 100, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.Plate), "Plaka", 130));
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.SpotCode), "Yer", 80));
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.VehicleText), "Araç", 120, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.Note), "Süre", 120));
        _grid.Columns.Add(new OndaGridColumn(nameof(MovementRow.Amount), "Tahsilat", 130, OndaColumnKind.Money) { ShowTotal = true });
        _trend.ValueFormat = OndaChart.Compact;
        Controls.AddRange(new Control[] { _occ, _revenue, _wash, _stay, _trend, _hours, _gridTitle, _grid });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new GetDashboard());
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var d = result.Value;

        Subtitle = $"{d.Now:dd MMMM yyyy, dddd HH:mm}  ·  içeride {d.Inside} araç ({d.SubscribersInside} abone)  ·  boş: {d.FreeCar} otomobil, {d.FreeLarge} büyük araç, {d.FreeMotorcycle} motosiklet  ·  F2 giriş, F3 çıkış, F4 yıkama";

        _occ.Value = (decimal)Math.Round(d.Occupancy * 100, 0);
        _occ.DeltaText = $"{d.Inside} / {d.Capacity - d.OutOfService} yer dolu";
        _occ.Delta = null;

        _revenue.Value = d.TodayTotal;
        _revenue.Delta = d.YesterdayToNowTotal == 0 ? null : (double)((d.TodayTotal - d.YesterdayToNowTotal) / d.YesterdayToNowTotal * 100);
        _revenue.Sparkline = d.ParkingTrend.Zip(d.WashTrend, (a, b) => a + b).TakeLast(14).ToArray();

        _wash.Value = d.WashWaiting + d.WashWashing + d.WashReady;
        _wash.DeltaText = $"{d.WashWaiting} bekliyor · {d.WashWashing} yıkanıyor · {d.WashReady} hazır";

        _stay.Value = (decimal)Math.Round(d.AverageStayMinutes, 0);
        _stay.DeltaText = $"{d.TodayEntries} giriş · {d.TodayExits} çıkış";

        _trend.Labels = d.TrendLabels;
        _trend.Series.Clear();
        _trend.Series.Add(new OndaSeries("Otopark", d.ParkingTrend));
        _trend.Series.Add(new OndaSeries("Yıkama", d.WashTrend, App.Cyan));

        _hours.Labels = d.HourLabels;
        _hours.Series.Clear();
        _hours.Series.Add(new OndaSeries("Bugün", d.EntriesByHourToday));
        _hours.Series.Add(new OndaSeries("Tipik gün", d.EntriesByHourTypical, Color.FromArgb(148, 163, 184)));

        _grid.DataSource = d.Movements;
        foreach (var card in new[] { _occ, _revenue, _wash, _stay }) card.Replay();
        _trend.Replay();
        _hours.Replay();
    }

    protected override void LayoutPage(Rectangle r)
    {
        var cw = (r.Width - Gap * 3) / 4;
        var cards = new[] { _occ, _revenue, _wash, _stay };
        for (var i = 0; i < 4; i++) cards[i].SetBounds(r.X + i * (cw + Gap), r.Y, cw, 150);

        var y = r.Y + 150 + Gap;
        var chartH = Math.Max(240, Math.Min(320, (r.Bottom - y) / 2));
        var leftW = (int)((r.Width - Gap) * 0.58);
        _trend.SetBounds(r.X, y, leftW, chartH);
        _hours.SetBounds(r.X + leftW + Gap, y, r.Width - leftW - Gap, chartH);

        y += chartH + Gap;
        _gridTitle.Location = new Point(r.X - 2, y + 2);
        y += 36;
        _grid.SetBounds(r.X, y, r.Width, Math.Max(140, r.Bottom - y));
    }
}
