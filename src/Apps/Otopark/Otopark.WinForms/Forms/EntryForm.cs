using OnderCode.Kernel.Validation;
using Otopark.Application;
using Otopark.Domain;

namespace Otopark.WinForms;

/// <summary>
/// Araç girişi: plaka, araç sınıfı, park yeri (boş bırakılırsa en uygun yer) ve istenirse yıkama paketi.
/// Abone plakası kayıtta tanınır.
/// </summary>
sealed class EntryForm : OndaForm
{
    const int M = 28;
    static readonly VehicleClass[] Vehicles = [VehicleClass.Car, VehicleClass.Large, VehicleClass.Motorcycle];

    readonly OndaTextBox _plate = new() { Label = "Plaka", Required = true, Placeholder = "34 XWQ 123", Icon = "", MaxLength = 12 };
    readonly OndaSegmented _vehicle = new() { Items = ["Otomobil", "Büyük araç", "Motosiklet"] };
    readonly OndaLookup _spot = new() { Label = "Park yeri", DisplayMember = nameof(SpotOption.Display), ValueMember = nameof(SpotOption.SpotId), PopupWidth = 360, Placeholder = "Otomatik: en uygun boş yer", Icon = "" };
    readonly OndaComboBox _package = new() { Label = "Yıkama", Placeholder = "Yıkama yok" };
    readonly OndaTextBox _note = new() { Label = "Yıkama notu", Placeholder = "Koltuk kılıfları, köpek tüyü…" };

    readonly OndaCard _infoCard = new() { Title = "Tarife", Icon = "" };
    readonly OndaLabel _info = new() { Style = OndaLabelStyle.Body };

    readonly OndaButton _cancel = new() { Action = OndaAction.Cancel, DialogResult = DialogResult.Cancel, Width = 120 };
    readonly OndaButton _save = new() { Action = OndaAction.Save, Text = "Girişi kaydet", Shortcut = Keys.F2, Width = 190 };

    readonly int? _presetSpot;
    IReadOnlyList<PackageOption> _packages = [];

    public EntryResult? Result { get; private set; }

    /// <summary>Formu açar; giriş yapıldıysa sonucu döner.</summary>
    public static EntryResult? Open(IWin32Window? owner, int? spotId = null, VehicleClass vehicle = VehicleClass.Car)
    {
        using var form = new EntryForm(spotId, vehicle);
        return form.ShowDialog(owner) == DialogResult.OK ? form.Result : null;
    }

    public EntryForm(int? spotId = null, VehicleClass vehicle = VehicleClass.Car)
    {
        _presetSpot = spotId;
        Text = "Araç girişi";
        TitleIcon = "";
        ShowMinimizeButton = ShowMaximizeButton = false;
        Resizable = false;
        CloseOnEscape = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(860, 560);

        _vehicle.SelectedIndex = Array.IndexOf(Vehicles, vehicle);
        _spot.Columns.Add(new OndaGridColumn(nameof(SpotOption.Code), "Yer", 80));
        _spot.Columns.Add(new OndaGridColumn(nameof(SpotOption.Zone), "Bölge", 70));
        _spot.Columns.Add(new OndaGridColumn(nameof(SpotOption.KindText), "Tür", 120));
        _plate.ValidateValue += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Value) && !TurkishPlate.IsValid(e.Value)) e.Error = "Geçerli bir plaka girin (ör. 34 XWQ 123)."; };
        _plate.Leave += (_, _) => { if (TurkishPlate.Normalize(_plate.Text) is { } p) _plate.Text = p; };

        _info.MaximumSize = new Size(280, 0);
        _infoCard.Controls.Add(_info);
        Controls.AddRange(new Control[] { _plate, _vehicle, _spot, _package, _note, _infoCard, _cancel, _save });

        _vehicle.SelectedIndexChanged += async (_, _) => { await LoadSpotsAsync(); FillPackages(); ShowInfo(); };
        _package.SelectedValueChanged += (_, _) => ShowInfo();
        _save.Click += async (_, _) => await SaveAsync();
        DoLayout();
    }

    VehicleClass Vehicle => Vehicles[Math.Max(0, _vehicle.SelectedIndex)];

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var packages = await App.Send(new GetWashPackages());
        if (packages.IsSuccess) _packages = packages.Value;
        FillPackages();
        await LoadSpotsAsync();
        ShowInfo();
        _plate.FocusInput();
    }

    async Task LoadSpotsAsync()
    {
        var keep = _spot.SelectedValue as int? ?? _presetSpot;
        var result = await App.Send(new GetFreeSpots(Vehicle));
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        _spot.DataSource = result.Value;
        _spot.HelperText = result.Value.Count == 0 ? "Bu araç sınıfı için boş yer yok" : $"{result.Value.Count} boş yer; boş bırakılırsa {result.Value[0].Code}";
        _spot.SelectedValue = keep is int id && result.Value.Any(s => s.SpotId == id) ? id : null;
    }

    void FillPackages()
    {
        var current = _package.SelectedItem as string;
        _package.Items.Clear();
        _package.Items.Add("Yıkama yok");
        foreach (var p in _packages)
            if (p.PriceFor(Vehicle) is { } price) _package.Items.Add($"{p.Name} · {price:N0} ₺");
        _package.SelectedItem = current is not null && _package.Items.Contains(current) ? current : "Yıkama yok";
    }

    PackageOption? SelectedPackage => _package.SelectedItem is string s && s != "Yıkama yok"
        ? _packages.FirstOrDefault(p => s.StartsWith(p.Name + " ·", StringComparison.Ordinal))
        : null;

    void ShowInfo()
    {
        var t = Tariff.For(Vehicle);
        var lines = new List<string>
        {
            Texts.Of(Vehicle),
            $"İlk {t.GraceMinutes} dakika ücretsiz",
            $"İlk saat {t.FirstHour:N0} ₺, sonraki her saat {t.ExtraHour:N0} ₺",
            $"Günlük en çok {t.DailyCap:N0} ₺",
            "",
            "Abone plakası girişte tanınır; ücret alınmaz.",
        };
        if (SelectedPackage is { } p)
        {
            lines.Add("");
            lines.Add($"{p.Name}: {p.PriceFor(Vehicle):N0} ₺, yaklaşık {p.Minutes} dk");
            lines.Add($"Yıkama yaptırana ilk {TariffEngine.WashFreeMinutes / 60} saat otopark ücretsiz.");
        }
        _info.Text = string.Join("\n", lines);
        _note.Enabled = SelectedPackage is not null;
    }

    async Task SaveAsync()
    {
        _plate.SetError(null);
        if (!OndaValidate.All(this, out var bad))
        {
            bad?.FocusInput();
            OndaToast.Error("Kırmızı işaretli alanları düzeltin.", "Kaydedilemedi");
            return;
        }
        _save.Busy = true;
        var result = await App.Send(new RegisterEntry(_plate.Text, Vehicle, _spot.SelectedValue as int?, SelectedPackage?.Id,
            SelectedPackage is null ? null : _note.Text));
        _save.Busy = false;
        if (!result.IsSuccess)
        {
            if (result.Errors.FirstOrDefault(e => e.Field == nameof(RegisterEntry.Plate)) is { } pe) _plate.SetError(pe.Message);
            App.ShowErrors(result, "Giriş kaydedilemedi");
            await LoadSpotsAsync();
            return;
        }
        Result = result.Value;
        var r = result.Value;
        OndaToast.Success(r.IsSubscriber ? $"{r.Plate} → {r.SpotCode} · abone: {r.SubscriberName}" : $"{r.Plate} → {r.SpotCode}", "Giriş yapıldı");
        DialogResult = DialogResult.OK;
    }

    /// <summary>Ekran görüntüsü için örnek değerler.</summary>
    public async Task FillSampleAsync()
    {
        _plate.Text = "34 XW 2026";
        await LoadSpotsAsync();
        FillPackages();
        if (_package.Items.Count > 2) _package.SelectedItem = _package.Items[2];
        _note.Text = "Bagaj da temizlensin";
        ShowInfo();
    }

    void DoLayout()
    {
        const int cardW = 320;
        var top = CaptionHeight + 22;
        var formW = ClientSize.Width - M * 3 - cardW;
        _plate.SetBounds(M, top, formW, _plate.Height);
        var y = _plate.Bottom + 10;
        _vehicle.SetBounds(M, y, Math.Min(formW, 420), 38);
        y += 52;
        _spot.SetBounds(M, y, formW, _spot.Height);
        y = _spot.Bottom + 6;
        _package.SetBounds(M, y, formW, _package.Height);
        y = _package.Bottom + 6;
        _note.SetBounds(M, y, formW, _note.Height);

        _infoCard.SetBounds(ClientSize.Width - M - cardW, top, cardW, 330);
        _info.Location = new Point(18, _infoCard.ContentTop + 4);

        var by = ClientSize.Height - 36 - 22;
        _save.Location = new Point(ClientSize.Width - M - _save.Width, by);
        _cancel.Location = new Point(_save.Left - 10 - _cancel.Width, by);
    }
}
