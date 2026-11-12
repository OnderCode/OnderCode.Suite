namespace Eczane.WinForms;

/// <summary>Personel kartı: ad, görev, işe giriş, brüt maaş. Kişisel iletişim ve kimlik bilgisi tutulmaz.</summary>
sealed class EmployeeForm : DialogBase
{
    static readonly StaffRole[] Roles = Enum.GetValues<StaffRole>();
    readonly int? _id;
    readonly OndaTextBox _name = new() { Label = "Ad soyad", Required = true, MaxLength = 100, Icon = "\uE77B" };
    readonly OndaComboBox _role = new() { Label = "Görev", Required = true };
    readonly OndaDatePicker _hire = new() { Label = "İşe giriş", Required = true };
    readonly OndaTextBox _gross = new() { Label = "Aylık brüt (₺)", Required = true, Kind = OndaInputKind.Money };
    readonly OndaLabel _hint = new() { Style = OndaLabelStyle.Muted, Text = "Yıllık izin hakkı kıdeme göre hesaplanır: 1-5 yıl 14, 5-15 yıl 20, 15 yıl ve üstü 26 iş günü. Nöbete yalnızca eczacılar yazılır." };

    public static bool Open(IWin32Window? owner, EmployeeRow? row = null)
    {
        using var f = new EmployeeForm(row);
        return f.ShowDialog(owner) == DialogResult.OK;
    }

    EmployeeForm(EmployeeRow? row) : base(row is null ? "Yeni personel" : row.FullName, "\uE77B", 600)
    {
        _id = row?.Id;
        foreach (var r in Roles) _role.Items.Add(Texts.Of(r));
        _role.SelectedItem = Texts.Of(row?.Role ?? StaffRole.Assistant);
        _hire.Value = At(row?.HireDate ?? App.Today);
        if (row is not null) { _name.Text = row.FullName; _gross.Number = row.MonthlyGross; }
        SaveButton.Text = "Personeli kaydet";
        Row(_name);
        Row(_role, _hire);
        Row(_gross);
        Row(_hint);
        Build();
    }

    protected override async Task<bool> OnSaveAsync()
    {
        var role = Roles.First(r => Texts.Of(r) == _role.SelectedItem as string);
        var r = await App.Send(new SaveEmployee(_id, _name.Text.Trim(), role, DateOf(_hire) ?? App.Today, _gross.Number ?? 0));
        if (!Check(r, "Personel kaydedilemedi")) return false;
        OndaToast.Success(_name.Text.Trim(), _id is null ? "Personel eklendi" : "Personel güncellendi");
        return true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _name.FocusInput();
    }
}

/// <summary>İzin girişi: yıllık izin hakkı aşılamaz, çakışan izin girilemez; izin günlerindeki nöbetler boşaltılır.</summary>
sealed class LeaveForm : DialogBase
{
    static readonly LeaveType[] Types = [LeaveType.Annual, LeaveType.Sick, LeaveType.Excuse, LeaveType.Unpaid];
    readonly OndaComboBox _employee = new() { Label = "Personel", Required = true, DisplayMember = nameof(EmployeeRow.FullName), ValueMember = nameof(EmployeeRow.Id) };
    readonly OndaSegmented _type = new() { Items = ["Yıllık izin", "Rapor", "Mazeret", "Ücretsiz"], SelectedIndex = 0 };
    readonly OndaDatePicker _start = new() { Label = "Başlangıç", Required = true };
    readonly OndaDatePicker _end = new() { Label = "Bitiş", Required = true };
    readonly OndaTextBox _note = new() { Label = "Not", MaxLength = 150 };
    readonly OndaLabel _info = new() { Style = OndaLabelStyle.Muted };
    readonly IReadOnlyList<EmployeeRow> _staff;

    public static bool Open(IWin32Window? owner, IReadOnlyList<EmployeeRow> staff, EmployeeRow? selected)
    {
        using var f = new LeaveForm(staff, selected);
        return f.ShowDialog(owner) == DialogResult.OK;
    }

    LeaveForm(IReadOnlyList<EmployeeRow> staff, EmployeeRow? selected) : base("İzin girişi", "\uE787", 600)
    {
        _staff = staff;
        _employee.DataSource = staff;
        _employee.SelectedValue = selected?.Id ?? staff.FirstOrDefault()?.Id;
        _start.Value = At(App.Today.AddDays(1));
        _end.Value = At(App.Today.AddDays(5));
        _employee.SelectedValueChanged += (_, _) => ShowInfo();
        _start.ValueChanged += (_, _) => { if (_start.Value is { } s && (_end.Value is null || _end.Value < s)) _end.Value = s; ShowInfo(); };
        _end.ValueChanged += (_, _) => ShowInfo();
        _type.SelectedIndexChanged += (_, _) => ShowInfo();
        SaveButton.Text = "İzni kaydet";
        Row(_employee);
        Row(_type);
        Row(_start, _end);
        Row(_note);
        Row(_info);
        ShowInfo();
        Build();
    }

    void ShowInfo()
    {
        var days = DateOf(_start) is { } s && DateOf(_end) is { } e && e >= s ? LeaveRecord.WorkDays(s, e) : 0;
        var emp = _staff.FirstOrDefault(x => x.Id == (_employee.SelectedValue as int?));
        _info.Text = emp is null ? "" : $"{days} iş günü (pazar sayılmaz)  ·  {emp.FullName}: yıllık izin hakkı {emp.LeaveEntitlement} gün, kullanılan {emp.LeaveUsed}, kalan {emp.LeaveLeft}";
    }

    protected override async Task<bool> OnSaveAsync()
    {
        if (_employee.SelectedValue is not int id) { OndaToast.Info("Personel seçin."); return false; }
        var r = await App.Send(new AddLeave(id, Types[Math.Max(0, _type.SelectedIndex)], DateOf(_start)!.Value, DateOf(_end)!.Value, string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim()));
        if (!Check(r, "İzin kaydedilemedi")) return false;
        OndaToast.Success("İzin günlerindeki nöbetler boşaltıldıysa nöbet çizelgesinden yeniden planlayın.", "İzin kaydedildi");
        return true;
    }
}
