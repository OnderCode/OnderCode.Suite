namespace Eczane.WinForms;

/// <summary>İlaç / ürün kartı: yeni kayıt ya da düzenleme. Barkod EAN-13 olarak doğrulanır; 12 hane yazılırsa kontrol hanesi tamamlanır.</summary>
sealed class ProductForm : DialogBase
{
    static readonly ProductCategory[] Categories = Enum.GetValues<ProductCategory>();
    static readonly PrescriptionColor[] Colors = [PrescriptionColor.White, PrescriptionColor.Red, PrescriptionColor.Green, PrescriptionColor.Purple, PrescriptionColor.Orange];

    readonly int? _id;
    readonly OndaTextBox _barcode = new() { Label = "Barkod", Required = true, MaxLength = 13, Kind = OndaInputKind.Integer, Icon = "\uE8EC", HelperText = "12 hane yazarsanız kontrol hanesi eklenir" };
    readonly OndaTextBox _name = new() { Label = "Ürün adı", Required = true, MaxLength = 150 };
    readonly OndaComboBox _category = new() { Label = "Ürün grubu", Required = true };
    readonly OndaTextBox _ingredient = new() { Label = "Etken madde", MaxLength = 120 };
    readonly OndaTextBox _group = new() { Label = "Tedavi grubu", MaxLength = 60, Placeholder = "ör. Antihipertansif" };
    readonly OndaSegmented _color = new() { Items = ["Beyaz", "Kırmızı", "Yeşil", "Mor", "Turuncu"], SelectedIndex = 0 };
    readonly OndaCheckBox _cold = new() { Text = "Soğuk zincir (+2/+8 °C)" };
    readonly OndaCheckBox _reimbursed = new() { Text = "SGK geri ödeme listesinde", Checked = true };
    readonly OndaTextBox _sale = new() { Label = "Satış fiyatı (₺)", Required = true, Kind = OndaInputKind.Money };
    readonly OndaTextBox _purchase = new() { Label = "Alış fiyatı (₺)", Required = true, Kind = OndaInputKind.Money };
    readonly OndaTextBox _min = new() { Label = "En az stok", Kind = OndaInputKind.Integer, Text = "3" };
    readonly OndaTextBox _shelf = new() { Label = "Raf", MaxLength = 12, Placeholder = "ör. A-12" };

    public int? SavedId { get; private set; }

    public static int? Open(IWin32Window? owner, ProductCard? card = null)
    {
        using var f = new ProductForm(card);
        return f.ShowDialog(owner) == DialogResult.OK ? f.SavedId : null;
    }

    ProductForm(ProductCard? card) : base(card is null ? "Yeni ürün" : card.Name, "\uE8F1", 680)
    {
        _id = card?.Id;
        foreach (var c in Categories) _category.Items.Add(Texts.Of(c));
        _category.SelectedItem = Texts.Of(ProductCategory.Rx);
        _barcode.Leave += (_, _) => { if (_barcode.Text.Length == 12 && Ean13.Complete(_barcode.Text) is { } full) _barcode.Text = full; };
        _barcode.ValidateValue += (_, e) => { if (e.Value.Length > 0 && !Ean13.IsValid(e.Value)) e.Error = "Geçerli bir EAN-13 barkodu girin."; };
        _category.SelectedValueChanged += (_, _) => ApplyCategory();
        if (card is not null)
        {
            _barcode.Text = card.Barcode;
            _barcode.ReadOnly = true;
            _name.Text = card.Name;
            _category.SelectedItem = Texts.Of(card.Category);
            _category.Enabled = false;
            _ingredient.Text = card.ActiveIngredient ?? "";
            _group.Text = card.Group ?? "";
            _color.SelectedIndex = Array.IndexOf(Colors, card.Color);
            _cold.Checked = card.ColdChain;
            _reimbursed.Checked = card.Reimbursed;
            _sale.Number = card.SalePrice;
            _purchase.Number = card.PurchasePrice;
            _min.Text = card.MinStock.ToString();
            _shelf.Text = card.Shelf ?? "";
            _ingredient.ReadOnly = _group.ReadOnly = true;
            _color.Enabled = _cold.Enabled = _reimbursed.Enabled = false;
        }
        SaveButton.Text = card is null ? "Ürünü kaydet" : "Değişiklikleri kaydet";
        Row(_barcode, _category);
        Row(_name);
        Row(_ingredient, _group);
        Row(_color);
        Row(_cold, _reimbursed);
        Row(_sale, _purchase);
        Row(_min, _shelf);
        Build();
        ApplyCategory();
    }

    ProductCategory Category => Categories.FirstOrDefault(c => Texts.Of(c) == _category.SelectedItem as string, ProductCategory.Rx);

    void ApplyCategory()
    {
        var rx = Category == ProductCategory.Rx;
        if (_id is null) _color.Enabled = rx;
        if (!rx && _id is null) _color.SelectedIndex = 0;
    }

    protected override async Task<bool> OnSaveAsync()
    {
        var r = await App.Send(new SaveProduct(_id, _barcode.Text.Trim(), _name.Text.Trim(), Category, NullIfEmpty(_ingredient.Text), NullIfEmpty(_group.Text),
            Colors[Math.Max(0, _color.SelectedIndex)], _cold.Checked, _reimbursed.Checked, _sale.Number ?? 0, _purchase.Number ?? 0,
            int.TryParse(_min.Text, out var m) ? m : 0, NullIfEmpty(_shelf.Text)));
        if (!Check(r, "Ürün kaydedilemedi")) return false;
        SavedId = r.Value;
        OndaToast.Success(_name.Text.Trim(), _id is null ? "Ürün eklendi" : "Ürün güncellendi");
        return true;
    }

    static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        (_id is null ? _barcode : _name).FocusInput();
    }
}
