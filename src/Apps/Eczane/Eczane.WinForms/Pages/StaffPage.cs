namespace Eczane.WinForms.Pages;

/// <summary>Personel ve izin: kıdem, izin hakkı ve kullanımı, nöbet sayısı ve puanı, son 30 gün satış; izin kayıtları.</summary>
sealed class StaffPage : PageBase
{
    readonly OndaSegmented _view = new() { Items = ["Personel", "İzinler"], SelectedIndex = 0 };
    readonly OndaButton _new = new() { Action = OndaAction.New, Text = "Yeni personel", Width = 160 };
    readonly OndaButton _edit = new() { Action = OndaAction.Edit, Kind = OndaButtonKind.Outline, Width = 120 };
    readonly OndaButton _dismiss = new() { Text = "İşten ayrılış", Icon = "\uE7E8", Kind = OndaButtonKind.Outline, Width = 150 };
    readonly OndaButton _leave = new() { Text = "İzin gir", Icon = "\uE787", Width = 130 };
    readonly OndaButton _deleteLeave = new() { Action = OndaAction.Delete, Text = "İzni sil", Kind = OndaButtonKind.Outline, Width = 120 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 100 };
    readonly OndaGrid _staff = new() { RowHeight = 36, AutoGenerateColumns = false };
    readonly OndaGrid _leaves = new() { RowHeight = 34, AutoGenerateColumns = false, EmptyText = "Bu yıl izin kaydı yok" };
    IReadOnlyList<EmployeeRow> _rows = [];

    public StaffPage() : base("Personel ve izin", "Yükleniyor…")
    {
        _staff.Columns.AddRange(
        [
            Col.Text(nameof(EmployeeRow.FullName), "Ad soyad", 180),
            Col.Badge(nameof(EmployeeRow.RoleText), "Görev", 160),
            Col.Badge(nameof(EmployeeRow.StatusText), "Durum", 100),
            Col.Day(nameof(EmployeeRow.HireDate), "İşe giriş", 100),
            Col.Int(nameof(EmployeeRow.YearsOfService), "Kıdem (yıl)", 90),
            Col.Int(nameof(EmployeeRow.LeaveEntitlement), "İzin hakkı", 90),
            Col.Int(nameof(EmployeeRow.LeaveUsed), "Kullanılan", 90, true),
            Col.Int(nameof(EmployeeRow.LeaveLeft), "Kalan", 70, true),
            Col.Int(nameof(EmployeeRow.SickDays), "Rapor", 70, true),
            Col.Int(nameof(EmployeeRow.DutiesThisYear), "Nöbet (yıl)", 90, true),
            Col.Int(nameof(EmployeeRow.DutyPoints), "Nöbet puanı", 100),
            Col.Day(nameof(EmployeeRow.NextDuty), "Sıradaki nöbet", 120),
            Col.Int(nameof(EmployeeRow.Sales30), "Satış 30 g.", 90, true),
            Col.Money(nameof(EmployeeRow.Revenue30), "Ciro 30 g.", 120, true),
            Col.Money(nameof(EmployeeRow.MonthlyGross), "Brüt maaş", 120, true),
        ]);
        _staff.RowTint = o => o is EmployeeRow { IsActive: false } ? Color.FromArgb(22, 148, 163, 184) : o is EmployeeRow { OnLeaveToday: true } ? Color.FromArgb(24, 245, 158, 11) : null;
        _leaves.Columns.AddRange(
        [
            Col.Text(nameof(LeaveRow.Employee), "Personel", 200),
            Col.Badge(nameof(LeaveRow.TypeText), "İzin türü", 130),
            Col.Day(nameof(LeaveRow.Start), "Başlangıç", 110),
            Col.Day(nameof(LeaveRow.End), "Bitiş", 110),
            Col.Int(nameof(LeaveRow.Days), "İş günü", 90, true),
            Col.Text(nameof(LeaveRow.Note), "Not", 300),
        ]);
        _view.SelectedIndexChanged += (_, _) => { Relayout(); };
        _new.Click += async (_, _) => { if (EmployeeForm.Open(FindForm())) await ReloadAsync(); };
        _edit.Click += async (_, _) => { if (_staff.SelectedItem is EmployeeRow e && EmployeeForm.Open(FindForm(), e)) await ReloadAsync(); };
        _staff.RowActivated += async (_, e) => { if (e.Item is EmployeeRow r && EmployeeForm.Open(FindForm(), r)) await ReloadAsync(); };
        _dismiss.Click += async (_, _) => await DismissAsync();
        _leave.Click += async (_, _) => { if (LeaveForm.Open(FindForm(), [.. _rows.Where(r => r.IsActive)], _staff.SelectedItem as EmployeeRow)) await ReloadAsync(); };
        _deleteLeave.Click += async (_, _) => await DeleteLeaveAsync();
        _excel.Click += (_, _) => { if (_view.SelectedIndex == 0) _staff.ExportExcelWithDialog("Personel"); else _leaves.ExportExcelWithDialog("İzinler"); };
        Controls.AddRange(new Control[] { _view, _new, _edit, _dismiss, _leave, _deleteLeave, _excel, _staff, _leaves });
    }

    protected override async Task LoadAsync()
    {
        var staff = await App.Send(new ListEmployees(IncludeInactive: true));
        if (!staff.IsSuccess) { App.ShowErrors(staff); return; }
        _rows = staff.Value;
        _staff.DataSource = _rows;
        var leaves = await App.Send(new ListLeaves(App.Today.Year));
        if (leaves.IsSuccess) _leaves.DataSource = leaves.Value;
        var active = _rows.Where(r => r.IsActive).ToList();
        Subtitle = $"{active.Count} çalışan ({active.Count(r => r.IsPharmacist)} eczacı)  ·  bugün izinli: {(active.Any(r => r.OnLeaveToday) ? string.Join(", ", active.Where(r => r.OnLeaveToday).Select(r => r.FullName)) : "yok")}  ·  aylık brüt toplam {App.Money(active.Sum(r => r.MonthlyGross))}";
        Relayout();
    }

    async Task DismissAsync()
    {
        if (_staff.SelectedItem is not EmployeeRow e || !e.IsActive) { OndaToast.Info("Önce çalışan bir personel seçin."); return; }
        if (!OndaMessageBox.Ask(FindForm(), $"{e.FullName} bugün itibarıyla işten ayrılmış olarak işaretlensin mi? İleri tarihli nöbetleri boşaltılır.", "İşten ayrılış")) return;
        var r = await App.Send(new DismissEmployee(e.Id, App.Today));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        OndaToast.Success($"{e.FullName} ayrıldı olarak işaretlendi.");
        await ReloadAsync();
    }

    async Task DeleteLeaveAsync()
    {
        if (_leaves.SelectedItem is not LeaveRow l) { OndaToast.Info("Önce bir izin seçin."); return; }
        if (!OndaMessageBox.Ask(FindForm(), $"{l.Employee} · {l.TypeText} ({l.Start:dd.MM}–{l.End:dd.MM}) silinsin mi?", "İzni sil")) return;
        var r = await App.Send(new DeleteLeave(l.Id));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        await ReloadAsync();
    }

    protected override void LayoutPage(Rectangle r)
    {
        var staff = _view.SelectedIndex <= 0;
        _view.SetBounds(r.X, r.Y, 260, 38);
        _staff.Visible = _new.Visible = _edit.Visible = _dismiss.Visible = staff;
        _leaves.Visible = _deleteLeave.Visible = !staff;
        if (staff) RightAlign(r.Right, r.Y, _excel, _leave, _dismiss, _edit, _new);
        else RightAlign(r.Right, r.Y, _excel, _deleteLeave, _leave);
        (staff ? _staff : _leaves).SetBounds(r.X, r.Y + 54, r.Width, r.Height - 54);
    }
}
