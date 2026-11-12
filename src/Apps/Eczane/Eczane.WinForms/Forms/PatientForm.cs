namespace Eczane.WinForms;

/// <summary>Hasta kaydı. Kişisel veri tutulmaz: T.C. kimlik no, telefon ve adres alanı yoktur; yalnızca reçete için gerekenler.</summary>
sealed class PatientForm : DialogBase
{
    readonly int? _id;
    readonly OndaTextBox _name = new() { Label = "Ad soyad", Required = true, MaxLength = 100, Icon = "\uE77B" };
    readonly OndaTextBox _year = new() { Label = "Doğum yılı", Required = true, Kind = OndaInputKind.Integer, MaxLength = 4 };
    readonly OndaSegmented _gender = new() { Items = ["Kadın", "Erkek"], SelectedIndex = 0 };
    readonly OndaSegmented _insurance = new() { Items = ["SGK", "Özel sigorta", "Güvencesiz"], SelectedIndex = 0 };
    readonly OndaSegmented _status = new() { Items = ["Çalışan", "Emekli"], SelectedIndex = 0 };
    readonly OndaCheckBox _chronic = new() { Text = "Kronik hastalık raporu var (katılım payı alınmaz)" };
    readonly OndaTextBox _note = new() { Label = "Not", MaxLength = 300, Placeholder = "ör. ilaçlarını ayın 5'inde alır" };
    readonly OndaLabel _privacy = new() { Style = OndaLabelStyle.Muted, Text = "Bu uygulama T.C. kimlik numarası, telefon ve adres saklamaz." };

    public int? SavedId { get; private set; }

    public static int? Open(IWin32Window? owner, PatientRow? row = null)
    {
        using var f = new PatientForm(row);
        return f.ShowDialog(owner) == DialogResult.OK ? f.SavedId : null;
    }

    PatientForm(PatientRow? row) : base(row is null ? "Yeni hasta" : row.FullName, "\uE77B", 600)
    {
        _id = row?.Id;
        _year.ValidateValue += (_, e) => { if (!int.TryParse(e.Value, out var y) || y < 1910 || y > App.Today.Year) e.Error = $"1910-{App.Today.Year} arasında bir yıl girin."; };
        _insurance.SelectedIndexChanged += (_, _) => _status.Enabled = _insurance.SelectedIndex == 0;
        if (row is not null)
        {
            _name.Text = row.FullName;
            _year.Text = row.BirthYear.ToString();
            _gender.SelectedIndex = (int)row.Gender;
            _insurance.SelectedIndex = (int)row.Insurance;
            _status.SelectedIndex = (int)row.SgkStatus;
            _chronic.Checked = row.ChronicReport;
            _note.Text = row.Note ?? "";
        }
        _status.Enabled = _insurance.SelectedIndex == 0;
        SaveButton.Text = "Hastayı kaydet";
        Row(_name);
        Row(_year, _gender);
        Row(_insurance);
        Row(_status);
        Row(_chronic);
        Row(_note);
        Row(_privacy);
        Build();
    }

    protected override async Task<bool> OnSaveAsync()
    {
        var r = await App.Send(new SavePatient(_id, _name.Text.Trim(), int.Parse(_year.Text), (Gender)_gender.SelectedIndex, (InsuranceType)_insurance.SelectedIndex,
            (SgkStatus)Math.Max(0, _status.SelectedIndex), _chronic.Checked, string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim()));
        if (!Check(r, "Hasta kaydedilemedi")) return false;
        SavedId = r.Value;
        OndaToast.Success(_name.Text.Trim(), _id is null ? "Hasta eklendi" : "Hasta güncellendi");
        return true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _name.FocusInput();
    }
}
