namespace Eczane.WinForms.Pages;

/// <summary>Hastalar: kurum, rapor, reçete sayısı, harcama, son gelişi; hasta geçmişi (düzenli ilaçlar ve beklenen sonraki alım).</summary>
sealed class PatientsPage : PageBase
{
    readonly OndaTextBox _search = new() { Compact = true, Placeholder = "Hasta ara…", Icon = "\uE721", ShowClearButton = true };
    readonly OndaButton _new = new() { Action = OndaAction.New, Text = "Yeni hasta", Width = 150 };
    readonly OndaButton _edit = new() { Action = OndaAction.Edit, Kind = OndaButtonKind.Outline, Width = 120 };
    readonly OndaButton _history = new() { Text = "Hasta geçmişi", Icon = "\uE81C", Kind = OndaButtonKind.Outline, Width = 160 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 100 };
    readonly OndaGrid _grid = new() { RowHeight = 34, AutoGenerateColumns = false };
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 350 };

    public PatientsPage() : base("Hastalar", "Yükleniyor…")
    {
        _grid.Columns.AddRange(
        [
            Col.Text(nameof(PatientRow.FullName), "Hasta", 200),
            Col.Int(nameof(PatientRow.Age), "Yaş", 60),
            Col.Text(nameof(PatientRow.GenderText), "Cinsiyet", 80),
            Col.Badge(nameof(PatientRow.InsuranceText), "Kurum", 130),
            Col.Badge(nameof(PatientRow.ChronicText), "Rapor", 90),
            Col.Int(nameof(PatientRow.Prescriptions), "Reçete", 80, true),
            Col.Int(nameof(PatientRow.Purchases), "Alışveriş", 90, true),
            Col.Money(nameof(PatientRow.Spent), "Ödediği", 120, true),
            Col.Date(nameof(PatientRow.LastVisit), "Son geliş", 110, "dd.MM.yyyy"),
            Col.Text(nameof(PatientRow.TopHospital), "En sık kurum", 220),
            Col.Text(nameof(PatientRow.Note), "Not", 220),
        ]);
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await ReloadAsync(); };
        _new.Click += async (_, _) => { if (PatientForm.Open(FindForm()) is not null) await ReloadAsync(); };
        _edit.Click += async (_, _) => { if (_grid.SelectedItem is PatientRow p && PatientForm.Open(FindForm(), p) is not null) await ReloadAsync(); };
        _history.Click += async (_, _) => { if (_grid.SelectedItem is PatientRow p) await ShowHistoryAsync(FindForm(), p.Id); else OndaToast.Info("Önce bir hasta seçin."); };
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog("Hastalar");
        _grid.RowActivated += async (_, e) => { if (e.Item is PatientRow p) await ShowHistoryAsync(FindForm(), p.Id); };
        Controls.AddRange(new Control[] { _search, _new, _edit, _history, _excel, _grid });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new ListPatients(_search.Text));
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var rows = result.Value;
        _grid.DataSource = rows;
        Subtitle = $"{rows.Count} hasta  ·  {rows.Count(p => p.ChronicReport)} raporlu  ·  {rows.Count(p => p.Insurance == InsuranceType.Sgk)} SGK, {rows.Count(p => p.Insurance == InsuranceType.Private)} özel sigorta  ·  T.C. kimlik, telefon ve adres tutulmaz  ·  geçmiş için çift tıklayın";
    }

    public static async Task ShowHistoryAsync(IWin32Window? owner, int patientId)
    {
        var r = await App.Send(new GetPatientHistory(patientId));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        var h = r.Value;
        var p = h.Patient;
        var head = $"{p.Age} yaş, {p.GenderText.ToLower(Texts.Turkish)}  ·  {p.InsuranceText}{(p.ChronicReport ? "  ·  kronik rapor var" : "")}  ·  en sık geldiği kurum: {p.TopHospital ?? "—"}" + (p.Note is { Length: > 0 } n ? $"\nNot: {n}" : "");
        var due = h.Medications.Where(m => m.NextExpected is { } d && d <= App.Today.AddDays(7)).ToList();
        if (due.Count > 0) head += $"\nBu hafta beklenen: {string.Join(", ", due.Select(m => $"{m.Product} ({m.NextExpected:dd.MM})"))}";
        using var f = new DetailForm(p.FullName, "\uE77B", head, size: new Size(1120, 760))
            .Card("Reçete", p.Prescriptions, "", $"{p.Purchases} alışveriş", "\uE8A5")
            .Card("Ödediği", p.Spent, "₺", "katılım payı + serbest alışveriş", "\uE8C7", App.Violet)
            .Card("Düzenli ilaç", h.Medications.Count(m => m.Times >= 3), "", "3 ve daha fazla kez alınan", "\uE95E", App.Teal)
            .Card("Son geliş", p.LastVisit is { } lv ? (App.Today.DayNumber - DateOnly.FromDateTime(lv).DayNumber) : 0, "", "gün önce", "\uE823", App.Amber)
            .Tab("İlaçları", h.Medications,
                Col.Text(nameof(PatientMedication.Product), "Ürün", 300),
                Col.Int(nameof(PatientMedication.Times), "Kaç kez", 80, true),
                Col.Int(nameof(PatientMedication.Boxes), "Kutu", 70, true),
                Col.Date(nameof(PatientMedication.LastAt), "Son alım", 110, "dd.MM.yyyy"),
                Col.Dbl(nameof(PatientMedication.AverageIntervalDays), "Ort. aralık (gün)", 140, "0"),
                Col.Day(nameof(PatientMedication.NextExpected), "Beklenen", 110))
            .Tab("Satışlar", h.Sales,
                Col.Date(nameof(SaleRow.SoldAt), "Tarih", 120, "dd.MM.yyyy HH:mm"),
                Col.Text(nameof(SaleRow.Number), "Fiş", 110),
                Col.Badge(nameof(SaleRow.KindText), "Tür", 110),
                Col.Text(nameof(SaleRow.Hospital), "Kurum", 200),
                Col.Text(nameof(SaleRow.Branch), "Branş", 130),
                Col.Badge(nameof(SaleRow.ColorText), "Renk", 80),
                Col.Int(nameof(SaleRow.Boxes), "Kutu", 60, true),
                Col.Money(nameof(SaleRow.PatientShare), "Hasta payı", 110, true),
                Col.Money(nameof(SaleRow.Total), "Tutar", 110, true),
                Col.Badge(nameof(SaleRow.StatusText), "Durum", 100));
        f.ShowDialog(owner);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }

    protected override void LayoutPage(Rectangle r)
    {
        _search.SetBounds(r.X, r.Y + 2, 320, _search.Height);
        RightAlign(r.Right, r.Y, _excel, _history, _edit, _new);
        _grid.SetBounds(r.X, r.Y + 54, r.Width, r.Height - 54);
    }
}
