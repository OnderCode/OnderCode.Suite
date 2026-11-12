namespace Eczane.WinForms;

/// <summary>Depoya ödeme: tutar, yöntem, tarih. Ödeme en eski vadeli borçtan başlayarak kapatılır.</summary>
sealed class PaymentForm : DialogBase
{
    static readonly SupplierPaymentMethod[] Methods = [SupplierPaymentMethod.Transfer, SupplierPaymentMethod.Check, SupplierPaymentMethod.Card];
    readonly WholesalerRow _w;
    readonly OndaLabel _info = new() { Style = OndaLabelStyle.Body };
    readonly OndaTextBox _amount = new() { Label = "Tutar (₺)", Required = true, Kind = OndaInputKind.Money };
    readonly OndaDatePicker _date = new() { Label = "Tarih", Required = true };
    readonly OndaSegmented _method = new() { Items = ["Havale/EFT", "Çek", "Kart"], SelectedIndex = 0 };
    readonly OndaTextBox _note = new() { Label = "Açıklama", MaxLength = 150 };

    PaymentForm(WholesalerRow w) : base($"Ödeme · {w.Name}", "\uE8C7", 560)
    {
        _w = w;
        _info.Text = $"Bakiye {App.Money(w.Balance)}  ·  vadesi geçen {App.Money(w.Overdue)}" + (w.NextDue is { } d ? $"  ·  {d:dd.MM.yyyy} vadeli {App.Money(w.NextDueAmount)}" : "");
        _amount.Number = w.Overdue > 0 ? w.Overdue : w.NextDueAmount > 0 ? w.NextDueAmount : w.Balance;
        _date.Value = At(App.Today);
        _date.MaxDate = _date.Value;
        SaveButton.Text = "Ödemeyi kaydet";
        Row(_info);
        Row(_amount, _date);
        Row(_method);
        Row(_note);
        Build();
    }

    public static bool Open(IWin32Window? owner, WholesalerRow w)
    {
        using var f = new PaymentForm(w);
        return f.ShowDialog(owner) == DialogResult.OK;
    }

    protected override async Task<bool> OnSaveAsync()
    {
        var r = await App.Send(new AddSupplierPayment(_w.Id, DateOf(_date) ?? App.Today, _amount.Number ?? 0, Methods[Math.Max(0, _method.SelectedIndex)], string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim()));
        if (!Check(r, "Ödeme kaydedilemedi")) return false;
        OndaToast.Success($"{_w.Name} · {App.Money(_amount.Number ?? 0)}", "Ödeme kaydedildi");
        return true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _amount.FocusInput();
    }
}
