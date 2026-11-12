namespace Eczane.WinForms.Pages;

/// <summary>Reçetelerin geldiği kurumlar: son 90 gün reçete, hasta, ciro, pay; son 30 gün ile önceki 30 gün karşılaştırması; en sık branş.</summary>
sealed class HospitalsPage : PageBase
{
    static readonly int[] Ranges = [30, 90, 365];
    readonly OndaSegmented _range = new() { Items = ["30 gün", "90 gün", "1 yıl"], SelectedIndex = 1 };
    readonly OndaButton _new = new() { Action = OndaAction.New, Text = "Yeni kurum", Width = 150 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 100 };
    readonly OndaChart _chart = new() { Title = "Reçete sayısına göre kurumlar", ChartType = OndaChartType.Bar, ShowLegend = false };
    readonly OndaChart _growth = new() { Title = "Son 30 gün · önceki 30 gün", Subtitle = "Reçete sayısı", ChartType = OndaChartType.Bar };
    readonly OndaGrid _grid = new() { RowHeight = 34, AutoGenerateColumns = false };

    public HospitalsPage() : base("Hastaneler", "Yükleniyor…")
    {
        _grid.Columns.AddRange(
        [
            Col.Text(nameof(HospitalRow.Name), "Kurum", 260),
            Col.Badge(nameof(HospitalRow.KindText), "Tür", 170),
            Col.Int(nameof(HospitalRow.Prescriptions), "Reçete", 80, true),
            Col.Int(nameof(HospitalRow.Patients), "Hasta", 80, true),
            Col.Progress(nameof(HospitalRow.Share), "Pay", 130),
            Col.Money(nameof(HospitalRow.Revenue), "Ciro", 130, true),
            Col.Money(nameof(HospitalRow.InstitutionShare), "Kurum payı", 130, true),
            Col.Money(nameof(HospitalRow.AverageRevenue), "Reçete başı", 110),
            Col.Int(nameof(HospitalRow.Last30), "Son 30 g.", 90, true),
            Col.Int(nameof(HospitalRow.Previous30), "Önceki 30 g.", 100, true),
            Col.Pct(nameof(HospitalRow.Growth), "Değişim", 90),
            Col.Text(nameof(HospitalRow.TopBranch), "En sık branş", 150),
        ]);
        _chart.ValueFormat = v => v.ToString("0");
        _growth.ValueFormat = v => v.ToString("0");
        _range.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _new.Click += async (_, _) => await NewAsync();
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog("Hastaneler");
        Controls.AddRange(new Control[] { _range, _new, _excel, _chart, _growth, _grid });
    }

    protected override async Task LoadAsync()
    {
        var days = Ranges[Math.Max(0, _range.SelectedIndex)];
        var result = await App.Send(new ListHospitals(days));
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var rows = result.Value;
        _grid.DataSource = rows;
        var top = rows.Where(h => h.Prescriptions > 0).Take(10).ToList();
        _chart.Subtitle = $"Son {days} gün · ilk {top.Count} kurum";
        _chart.Labels = [.. top.Select(h => Short(h.Name))];
        _chart.Series.Clear();
        _chart.Series.Add(new OndaSeries("Reçete", [.. top.Select(h => (double)h.Prescriptions)]));
        var g = rows.OrderByDescending(h => h.Last30).Take(6).ToList();
        _growth.Labels = [.. g.Select(h => Short(h.Name))];
        _growth.Series.Clear();
        _growth.Series.Add(new OndaSeries("Son 30 gün", [.. g.Select(h => (double)h.Last30)]));
        _growth.Series.Add(new OndaSeries("Önceki 30 gün", [.. g.Select(h => (double)h.Previous30)], App.Slate));
        var leader = rows.FirstOrDefault();
        var total = rows.Sum(h => h.Prescriptions);
        Subtitle = leader is null ? "Kayıt yok" : $"Son {days} günde {total:N0} reçete  ·  en çok reçete gelen kurum: {leader.Name} (%{leader.Share * 100:0.0})  ·  ilk üç kurum %{rows.Take(3).Sum(h => h.Share) * 100:0.0}";
        _chart.Replay();
        _growth.Replay();
    }

    static string Short(string name) => App.ShortHospital(name);

    async Task NewAsync()
    {
        if (InputForm.Ask(FindForm(), "Yeni kurum", "\uE95E", "Kurum adı", null, "Türü özel hastane olarak kaydedilir; reçete girerken seçilebilir.", OndaInputKind.Text,
            async name =>
            {
                var r = await App.Send(new SaveHospital(name, HospitalKind.Private));
                if (!r.IsSuccess) { App.ShowErrors(r); return false; }
                return true;
            }))
            await ReloadAsync();
    }

    protected override void LayoutPage(Rectangle r)
    {
        _range.SetBounds(r.X, r.Y, 300, 38);
        RightAlign(r.Right, r.Y, _excel, _new);
        var y = r.Y + 54;
        var chartH = Math.Max(230, Math.Min(300, (r.Bottom - y) / 2 - Gap));
        var leftW = (int)((r.Width - Gap) * 0.6);
        _chart.SetBounds(r.X, y, leftW, chartH);
        _growth.SetBounds(r.X + leftW + Gap, y, r.Width - leftW - Gap, chartH);
        y += chartH + Gap;
        _grid.SetBounds(r.X, y, r.Width, r.Bottom - y);
    }
}
