using OnderCode.Kernel.Validation;
using Otopark.Application;
using Otopark.Domain;

namespace Otopark.WinForms;

/// <summary>Yeni aylık abonelik.</summary>
sealed class SubscriptionForm : OndaForm
{
    const int M = 28;
    static readonly VehicleClass[] Vehicles = [VehicleClass.Car, VehicleClass.Large, VehicleClass.Motorcycle];
    static readonly int[] Months = [1, 3, 6, 12];

    readonly OndaTextBox _plate = new() { Label = "Plaka", Required = true, Placeholder = "34 XWQ 123", Icon = "", MaxLength = 12 };
    readonly OndaTextBox _owner = new() { Label = "Abone adı", Required = true, Icon = "" };
    readonly OndaSegmented _vehicle = new() { Items = ["Otomobil", "Büyük araç", "Motosiklet"] };
    readonly OndaSegmented _months = new() { Items = ["1 ay", "3 ay", "6 ay", "12 ay"] };
    readonly OndaLabel _price = new() { Style = OndaLabelStyle.Header };
    readonly OndaLabel _hint = new() { Style = OndaLabelStyle.Muted };
    readonly OndaButton _cancel = new() { Action = OndaAction.Cancel, DialogResult = DialogResult.Cancel, Width = 120 };
    readonly OndaButton _save = new() { Action = OndaAction.Save, Text = "Aboneliği başlat", Shortcut = Keys.F2, Width = 190 };

    public int? CreatedId { get; private set; }

    public static int? Open(IWin32Window? owner)
    {
        using var form = new SubscriptionForm();
        return form.ShowDialog(owner) == DialogResult.OK ? form.CreatedId : null;
    }

    public SubscriptionForm()
    {
        Text = "Yeni abonelik";
        TitleIcon = "";
        ShowMinimizeButton = ShowMaximizeButton = false;
        Resizable = false;
        CloseOnEscape = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(600, 520);

        _vehicle.SelectedIndex = 0;
        _months.SelectedIndex = 0;
        _plate.ValidateValue += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Value) && !TurkishPlate.IsValid(e.Value)) e.Error = "Geçerli bir plaka girin (ör. 34 XWQ 123)."; };
        _plate.Leave += (_, _) => { if (TurkishPlate.Normalize(_plate.Text) is { } p) _plate.Text = p; };
        Controls.AddRange(new Control[] { _plate, _owner, _vehicle, _months, _price, _hint, _cancel, _save });
        _vehicle.SelectedIndexChanged += (_, _) => ShowPrice();
        _months.SelectedIndexChanged += (_, _) => ShowPrice();
        _save.Click += async (_, _) => await SaveAsync();
        ShowPrice();
        DoLayout();
    }

    VehicleClass Vehicle => Vehicles[Math.Max(0, _vehicle.SelectedIndex)];
    int MonthCount => Months[Math.Max(0, _months.SelectedIndex)];

    void ShowPrice()
    {
        var monthly = Subscription.MonthlyFeeFor(Vehicle);
        _price.Text = App.Money(monthly * MonthCount);
        var start = App.Today;
        _hint.Text = $"Aylık {App.Money(monthly)} · {start:dd.MM.yyyy} – {start.AddMonths(MonthCount).AddDays(-1):dd.MM.yyyy}\nAbone plaka bu süre boyunca ücretsiz girer çıkar.";
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
        var result = await App.Send(new CreateSubscription(_plate.Text, _owner.Text, Vehicle, MonthCount));
        _save.Busy = false;
        if (!result.IsSuccess) { App.ShowErrors(result, "Abonelik açılamadı"); return; }
        CreatedId = result.Value;
        OndaToast.Success($"{TurkishPlate.Normalize(_plate.Text)} · {MonthCount} ay", "Abonelik başladı");
        DialogResult = DialogResult.OK;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _plate.FocusInput();
    }

    void DoLayout()
    {
        var top = CaptionHeight + 22;
        var w = ClientSize.Width - M * 2;
        _plate.SetBounds(M, top, w, _plate.Height);
        _owner.SetBounds(M, _plate.Bottom + 6, w, _owner.Height);
        var y = _owner.Bottom + 10;
        _vehicle.SetBounds(M, y, 400, 38);
        y += 50;
        _months.SetBounds(M, y, 400, 38);
        y += 58;
        _price.Location = new Point(M - 3, y);
        _hint.Location = new Point(M - 1, y + 40);
        _hint.MaximumSize = new Size(w, 0);
        var by = ClientSize.Height - 36 - 22;
        _save.Location = new Point(ClientSize.Width - M - _save.Width, by);
        _cancel.Location = new Point(_save.Left - 10 - _cancel.Width, by);
    }
}
