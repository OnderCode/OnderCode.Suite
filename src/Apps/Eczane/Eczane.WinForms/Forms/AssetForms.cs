namespace Eczane.WinForms;

/// <summary>Yeni demirbaş: kod, ad, grup, alış tarihi ve bedeli, faydalı ömür (doğrusal amortisman), hurda değeri, bakım periyodu.</summary>
sealed class AssetForm : DialogBase
{
    static readonly AssetCategory[] Categories = Enum.GetValues<AssetCategory>();
    readonly OndaTextBox _code = new() { Label = "Demirbaş kodu", Required = true, MaxLength = 20 };
    readonly OndaTextBox _name = new() { Label = "Ad", Required = true, MaxLength = 120 };
    readonly OndaComboBox _category = new() { Label = "Grup", Required = true };
    readonly OndaTextBox _location = new() { Label = "Konum", Required = true, MaxLength = 60, Placeholder = "ör. Arka depo" };
    readonly OndaTextBox _serial = new() { Label = "Seri no", MaxLength = 40 };
    readonly OndaDatePicker _date = new() { Label = "Alış tarihi", Required = true };
    readonly OndaTextBox _cost = new() { Label = "Bedel (₺)", Required = true, Kind = OndaInputKind.Money };
    readonly OndaTextBox _salvage = new() { Label = "Hurda değeri (₺)", Kind = OndaInputKind.Money, Text = "0" };
    readonly OndaTextBox _life = new() { Label = "Faydalı ömür (yıl)", Required = true, Kind = OndaInputKind.Integer, Text = "5" };
    readonly OndaTextBox _interval = new() { Label = "Bakım periyodu (ay)", Kind = OndaInputKind.Integer, Text = "12", HelperText = "0: periyodik bakım yok" };

    public static bool Open(IWin32Window? owner, string suggestedCode)
    {
        using var f = new AssetForm(suggestedCode);
        return f.ShowDialog(owner) == DialogResult.OK;
    }

    AssetForm(string code) : base("Yeni demirbaş", "\uE772", 640)
    {
        foreach (var c in Categories) _category.Items.Add(Texts.Of(c));
        _category.SelectedItem = Texts.Of(AssetCategory.Device);
        _code.Text = code;
        _date.Value = At(App.Today);
        _date.MaxDate = _date.Value;
        SaveButton.Text = "Demirbaşı kaydet";
        Row(_code, _category);
        Row(_name);
        Row(_location, _serial);
        Row(_date, _cost);
        Row(_life, _salvage);
        Row(_interval);
        Build();
    }

    protected override async Task<bool> OnSaveAsync()
    {
        var cat = Categories.First(c => Texts.Of(c) == _category.SelectedItem as string);
        var r = await App.Send(new SaveAsset(null, _code.Text.Trim().ToUpperInvariant(), _name.Text.Trim(), cat, DateOf(_date) ?? App.Today, _cost.Number ?? 0,
            int.TryParse(_life.Text, out var l) ? l : 0, _location.Text.Trim(), _salvage.Number ?? 0, int.TryParse(_interval.Text, out var i) ? i : 0,
            string.IsNullOrWhiteSpace(_serial.Text) ? null : _serial.Text.Trim()));
        if (!Check(r, "Demirbaş kaydedilemedi")) return false;
        OndaToast.Success(_name.Text.Trim(), "Demirbaş eklendi");
        return true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _name.FocusInput();
    }
}

/// <summary>Bakım, kalibrasyon ya da onarım kaydı; sonraki bakım tarihi ileri alınır, onarım cihazı yeniden hizmete alır.</summary>
sealed class MaintenanceForm : DialogBase
{
    readonly AssetRow _asset;
    readonly OndaSegmented _kind = new() { Items = ["Periyodik bakım", "Kalibrasyon", "Onarım"], SelectedIndex = 0 };
    readonly OndaDatePicker _date = new() { Label = "Tarih", Required = true };
    readonly OndaTextBox _cost = new() { Label = "Maliyet (₺)", Kind = OndaInputKind.Money, Text = "0" };
    readonly OndaTextBox _note = new() { Label = "Açıklama", MaxLength = 200, Placeholder = "ör. kompresör gazı tamamlandı" };

    public static bool Open(IWin32Window? owner, AssetRow asset)
    {
        using var f = new MaintenanceForm(asset);
        return f.ShowDialog(owner) == DialogResult.OK;
    }

    MaintenanceForm(AssetRow asset) : base($"Bakım · {asset.Name}", "\uE90F", 560)
    {
        _asset = asset;
        _date.Value = At(App.Today);
        _date.MaxDate = _date.Value;
        if (asset.Status == AssetStatus.InRepair) _kind.SelectedIndex = 2;
        SaveButton.Text = "Kaydet";
        Row(_kind);
        Row(_date, _cost);
        Row(_note);
        Build();
    }

    protected override async Task<bool> OnSaveAsync()
    {
        var r = await App.Send(new AddMaintenance(_asset.Id, DateOf(_date) ?? App.Today, (MaintenanceKind)_kind.SelectedIndex, _cost.Number ?? 0,
            string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim()));
        if (!Check(r, "Bakım kaydedilemedi")) return false;
        OndaToast.Success(_asset.Name, "Bakım kaydedildi");
        return true;
    }
}
