using OnderCode.Kernel.Validation;
using Otopark.Application;
using Otopark.Domain;

namespace Otopark.WinForms;

/// <summary>Yıkama siparişi. Plaka otoparktaysa sipariş o kalışa bağlanır ve ücret çıkışta alınır.</summary>
sealed class WashOrderForm : OndaForm
{
    const int M = 28;
    static readonly VehicleClass[] Vehicles = [VehicleClass.Car, VehicleClass.Large, VehicleClass.Motorcycle];

    readonly OndaTextBox _plate = new() { Label = "Plaka", Required = true, Placeholder = "34 XWQ 123", Icon = "", MaxLength = 12 };
    readonly OndaSegmented _vehicle = new() { Items = ["Otomobil", "Büyük araç", "Motosiklet"] };
    readonly OndaLookup _package = new() { Label = "Paket", Required = true, DisplayMember = nameof(PackageOption.Name), ValueMember = nameof(PackageOption.Id), PopupWidth = 520, Icon = "" };
    readonly OndaTextBox _note = new() { Label = "Not", Placeholder = "Koltuk kılıfları, köpek tüyü…" };
    readonly OndaLabel _price = new() { Style = OndaLabelStyle.Header };
    readonly OndaLabel _hint = new() { Style = OndaLabelStyle.Muted };
    readonly OndaButton _cancel = new() { Action = OndaAction.Cancel, DialogResult = DialogResult.Cancel, Width = 120 };
    readonly OndaButton _save = new() { Action = OndaAction.Save, Text = "Siparişi aç", Shortcut = Keys.F2, Width = 170 };

    IReadOnlyList<PackageOption> _packages = [];

    public int? CreatedId { get; private set; }

    public static int? Open(IWin32Window? owner, string? plate = null, VehicleClass vehicle = VehicleClass.Car)
    {
        using var form = new WashOrderForm(plate, vehicle);
        return form.ShowDialog(owner) == DialogResult.OK ? form.CreatedId : null;
    }

    public WashOrderForm(string? plate = null, VehicleClass vehicle = VehicleClass.Car)
    {
        Text = "Yıkama siparişi";
        TitleIcon = "";
        ShowMinimizeButton = ShowMaximizeButton = false;
        Resizable = false;
        CloseOnEscape = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 520);

        _plate.Text = plate ?? "";
        _vehicle.SelectedIndex = Array.IndexOf(Vehicles, vehicle);
        _package.Columns.Add(new OndaGridColumn(nameof(PackageOption.Name), "Paket", 180));
        _package.Columns.Add(new OndaGridColumn(nameof(PackageOption.Minutes), "Dk", 60, OndaColumnKind.Number));
        _package.Columns.Add(new OndaGridColumn(nameof(PackageOption.CarPrice), "Otomobil", 110, OndaColumnKind.Money));
        _package.Columns.Add(new OndaGridColumn(nameof(PackageOption.LargePrice), "Büyük araç", 110, OndaColumnKind.Money));
        _plate.ValidateValue += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Value) && !TurkishPlate.IsValid(e.Value)) e.Error = "Geçerli bir plaka girin (ör. 34 XWQ 123)."; };
        _plate.Leave += (_, _) => { if (TurkishPlate.Normalize(_plate.Text) is { } p) _plate.Text = p; };
        Controls.AddRange(new Control[] { _plate, _vehicle, _package, _note, _price, _hint, _cancel, _save });

        _vehicle.SelectedIndexChanged += (_, _) => ShowPrice();
        _package.SelectedValueChanged += (_, _) => ShowPrice();
        _save.Click += async (_, _) => await SaveAsync();
        DoLayout();
    }

    VehicleClass Vehicle => Vehicles[Math.Max(0, _vehicle.SelectedIndex)];

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var result = await App.Send(new GetWashPackages());
        if (result.IsSuccess) _package.DataSource = _packages = result.Value;
        ShowPrice();
        if (string.IsNullOrEmpty(_plate.Text)) _plate.FocusInput(); else _package.FocusInput();
    }

    void ShowPrice()
    {
        var p = _packages.FirstOrDefault(x => x.Id == (_package.SelectedValue as int? ?? 0));
        if (p is null) { _price.Text = ""; _hint.Text = "Paket seçin."; return; }
        var price = p.PriceFor(Vehicle);
        _price.Text = price is { } v ? App.Money(v) : "—";
        _hint.Text = price is null ? $"{p.Name} motosiklete uygulanmaz." : $"Yaklaşık {p.Minutes} dakika. Araç otoparktaysa ücret çıkışta alınır ve ilk 2 saat otopark ücretsiz olur.";
    }

    async Task SaveAsync()
    {
        if (!OndaValidate.All(this, out var bad))
        {
            bad?.FocusInput();
            OndaToast.Error("Kırmızı işaretli alanları düzeltin.", "Kaydedilemedi");
            return;
        }
        _save.Busy = true;
        var result = await App.Send(new CreateWashOrder(_plate.Text, Vehicle, _package.SelectedValue as int? ?? 0, _note.Text));
        _save.Busy = false;
        if (!result.IsSuccess) { App.ShowErrors(result, "Sipariş açılamadı"); return; }
        CreatedId = result.Value;
        OndaToast.Success($"{TurkishPlate.Normalize(_plate.Text)} kuyruğa eklendi.", "Yıkama siparişi");
        DialogResult = DialogResult.OK;
    }

    void DoLayout()
    {
        var top = CaptionHeight + 22;
        var w = ClientSize.Width - M * 2;
        _plate.SetBounds(M, top, w, _plate.Height);
        var y = _plate.Bottom + 10;
        _vehicle.SetBounds(M, y, 420, 38);
        y += 52;
        _package.SetBounds(M, y, w, _package.Height);
        y = _package.Bottom + 6;
        _note.SetBounds(M, y, w, _note.Height);
        y = _note.Bottom + 10;
        _price.Location = new Point(M - 3, y);
        _hint.Location = new Point(M - 1, y + 40);
        _hint.MaximumSize = new Size(w, 0);
        var by = ClientSize.Height - 36 - 22;
        _save.Location = new Point(ClientSize.Width - M - _save.Width, by);
        _cancel.Location = new Point(_save.Left - 10 - _cancel.Width, by);
    }
}
