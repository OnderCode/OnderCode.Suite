namespace Eczane.WinForms;

/// <summary>
/// Satış ekranı (POS). Reçeteli satışta hasta, kurum, branş, reçete rengi ve numarası istenir; ürünler barkod ya da adla eklenir.
/// Her değişiklikte satış uygulama katmanında fiyatlanır: FEFO ile hangi partiden düşüleceği, katılım payı ve kurum payı anında görünür.
/// </summary>
sealed class SaleForm : OndaForm
{
    const int M = 24;
    static readonly PrescriptionColor[] Colors = [PrescriptionColor.White, PrescriptionColor.Red, PrescriptionColor.Green, PrescriptionColor.Purple, PrescriptionColor.Orange];

    /// <summary>Sepet satırı (ekranda düzenlenir).</summary>
    sealed class CartLine
    {
        public int ProductId { get; init; }
        public string Name { get; init; } = "";
        public string CategoryText { get; init; } = "";
        public string ColorText { get; init; } = "";
        public decimal Price { get; init; }
        public int Stock { get; init; }
        public bool Reimbursed { get; init; }
        public int Quantity { get; set; } = 1;
        public decimal Total => Price * Quantity;
    }

    readonly SaleKind _kind;
    readonly List<CartLine> _cart = [];

    readonly OndaLookup _patient = new() { Label = "Hasta", Required = true, DisplayMember = nameof(PatientOption.Display), ValueMember = nameof(PatientOption.Id), PopupWidth = 520, Placeholder = "Adını yazın…", Icon = "\uE77B" };
    readonly OndaLookup _hospital = new() { Label = "Reçeteyi yazan kurum", Required = true, DisplayMember = nameof(HospitalOption.Name), ValueMember = nameof(HospitalOption.Id), PopupWidth = 460, Icon = "\uE95E" };
    readonly OndaComboBox _branch = new() { Label = "Hekim branşı", Required = true };
    readonly OndaSegmented _color = new() { Items = ["Beyaz", "Kırmızı", "Yeşil", "Mor", "Turuncu"], SelectedIndex = 0 };
    readonly OndaTextBox _rxNo = new() { Label = "e-Reçete no", Required = true, MaxLength = 20, Placeholder = "ör. 3K7T2PA" };
    readonly OndaDatePicker _rxDate = new() { Label = "Reçete tarihi", Required = true };

    readonly OndaLookup _product = new() { Label = "Ürün (barkod ya da ad)", DisplayMember = nameof(CatalogItem.Display), ValueMember = nameof(CatalogItem.Id), PopupWidth = 720, MaxVisibleRows = 12, Placeholder = "Barkodu okutun ya da yazın…", Icon = "\uE721" };
    readonly OndaTextBox _qty = new() { Label = "Kutu", Kind = OndaInputKind.Integer, Text = "1" };
    readonly OndaButton _add = new() { Text = "Ekle", Icon = "\uE710", Width = 110 };
    readonly OndaGrid _cartGrid = new() { RowHeight = 34, ShowFilterRow = false, AutoGenerateColumns = false, EmptyText = "Sepet boş" };
    readonly OndaButton _plus = new() { Text = "+1", Kind = OndaButtonKind.Outline, Width = 70 };
    readonly OndaButton _minus = new() { Text = "−1", Kind = OndaButtonKind.Outline, Width = 70 };
    readonly OndaButton _remove = new() { Text = "Kaldır", Icon = "\uE74D", Kind = OndaButtonKind.Outline, Width = 110 };

    readonly OndaCard _receipt = new() { Title = "Fiş", Icon = "\uE8A5" };
    readonly OndaGrid _lots = new() { RowHeight = 28, ShowFilterRow = false, ShowFooter = false, AutoGenerateColumns = false, EmptyText = "Ürün ekleyin" };
    readonly OndaLabel _lines = new() { Style = OndaLabelStyle.Body, AutoSize = true };
    readonly OndaLabel _due = new() { Style = OndaLabelStyle.Header, AutoSize = true };
    readonly OndaLabel _warn = new() { Style = OndaLabelStyle.Muted, AutoSize = true };
    readonly OndaSegmented _payment = new() { Items = ["Nakit", "Kart"], SelectedIndex = 1 };
    readonly OndaTextBox _discount = new() { Label = "İndirim (₺)", Kind = OndaInputKind.Money, Text = "0", HelperText = "En çok %15 · yalnızca reçetesiz ürünlerde" };
    readonly OndaComboBox _seller = new() { Label = "Satan", DisplayMember = nameof(EmployeeOption.FullName), ValueMember = nameof(EmployeeOption.Id) };

    readonly OndaButton _cancel = new() { Action = OndaAction.Cancel, DialogResult = DialogResult.Cancel, Width = 120 };
    readonly OndaButton _save = new() { Action = OndaAction.Save, Text = "Satışı tamamla", Shortcut = Keys.F9, Width = 200 };

    IReadOnlyList<CatalogItem> _catalog = [];
    int _quoteVersion;

    public SaleReceipt? Receipt { get; private set; }

    public static SaleReceipt? Open(IWin32Window? owner, SaleKind kind)
    {
        using var form = new SaleForm(kind);
        return form.ShowDialog(owner) == DialogResult.OK ? form.Receipt : null;
    }

    public SaleForm(SaleKind kind)
    {
        _kind = kind;
        Text = kind == SaleKind.Prescription ? "Reçeteli satış" : "Serbest satış";
        TitleIcon = kind == SaleKind.Prescription ? "\uE8A5" : "\uE719";
        ShowMinimizeButton = false;
        CloseOnEscape = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1240, kind == SaleKind.Prescription ? 800 : 700);
        MinimumSize = new Size(1100, 640);

        _patient.Columns.Add(new OndaGridColumn(nameof(PatientOption.FullName), "Hasta", 220));
        _patient.Columns.Add(new OndaGridColumn(nameof(PatientOption.BirthYear), "Doğum", 70));
        _patient.Columns.Add(Col.Badge(nameof(PatientOption.InsuranceText), "Kurum", 110));
        _patient.Columns.Add(Col.Check(nameof(PatientOption.ChronicReport), "Raporlu", 80));
        _hospital.Columns.Add(new OndaGridColumn(nameof(HospitalOption.Name), "Kurum", 260));
        _hospital.Columns.Add(Col.Badge(nameof(HospitalOption.KindText), "Tür", 170));
        foreach (var b in Branches.All) _branch.Items.Add(b);

        _product.Columns.Add(new OndaGridColumn(nameof(CatalogItem.Barcode), "Barkod", 130));
        _product.Columns.Add(new OndaGridColumn(nameof(CatalogItem.Name), "Ürün", 290));
        _product.Columns.Add(Col.Badge(nameof(CatalogItem.CategoryText), "Grup", 120));
        _product.Columns.Add(Col.Int(nameof(CatalogItem.Stock), "Stok", 60));
        _product.Columns.Add(Col.Money(nameof(CatalogItem.Price), "Fiyat", 100));

        _cartGrid.Columns.AddRange(
        [
            Col.Text(nameof(CartLine.Name), "Ürün", 250),
            Col.Badge(nameof(CartLine.CategoryText), "Grup", 110),
            Col.Badge(nameof(CartLine.ColorText), "Renk", 80),
            Col.Int(nameof(CartLine.Stock), "Stok", 55),
            Col.Int(nameof(CartLine.Quantity), "Kutu", 55, true),
            Col.Money(nameof(CartLine.Price), "Fiyat", 100),
            Col.Money(nameof(CartLine.Total), "Tutar", 110, true),
        ]);
        _lots.Columns.AddRange(
        [
            Col.Text(nameof(ReceiptLine.Product), "Ürün", 140),
            Col.Text(nameof(ReceiptLine.LotNo), "Parti", 76),
            Col.Day(nameof(ReceiptLine.Expiry), "SKT", 84),
            Col.Int(nameof(ReceiptLine.Quantity), "Kutu", 44),
        ]);

        _rxDate.Value = App.Today.ToDateTime(TimeOnly.MinValue);
        _rxDate.MaxDate = _rxDate.Value;
        var rx = kind == SaleKind.Prescription;
        foreach (var c in new Control[] { _patient, _hospital, _branch, _color, _rxNo, _rxDate }) c.Visible = c.Enabled = rx;
        _discount.Visible = !rx;

        _receipt.Controls.AddRange(new Control[] { _lots, _lines, _due, _warn });
        Controls.AddRange(new Control[] { _patient, _hospital, _branch, _color, _rxNo, _rxDate, _product, _qty, _add, _cartGrid, _plus, _minus, _remove, _receipt, _payment, _discount, _seller, _cancel, _save });

        _product.SelectedValueChanged += (_, _) => { if (_product.SelectedItem is not null) _qty.FocusInput(); };
        _qty.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddSelected(); } };
        _add.Click += (_, _) => AddSelected();
        _plus.Click += (_, _) => Change(+1);
        _minus.Click += (_, _) => Change(-1);
        _remove.Click += (_, _) => { if (_cartGrid.SelectedItem is CartLine l) { _cart.Remove(l); CartChanged(); } };
        _patient.SelectedValueChanged += async (_, _) => await QuoteAsync();
        _color.SelectedIndexChanged += async (_, _) => await QuoteAsync();
        _discount.Leave += async (_, _) => await QuoteAsync();
        _save.Click += async (_, _) => await SaveAsync();
        Resize += (_, _) => DoLayout();
        DoLayout();
        ShowQuote(null, null);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadOptionsAsync();
        if (_kind == SaleKind.Prescription) _patient.FocusInput(); else _product.FocusInput();
    }

    public async Task LoadOptionsAsync()
    {
        var catalog = await App.Send(new GetSaleCatalog());
        if (catalog.IsSuccess) _product.DataSource = _catalog = _kind == SaleKind.Retail ? [.. catalog.Value.Where(c => !c.IsRx)] : catalog.Value;
        var staff = await App.Send(new GetEmployeeOptions());
        if (staff.IsSuccess)
        {
            _seller.DataSource = staff.Value;
            _seller.SelectedValue = staff.Value.FirstOrDefault()?.Id;
        }
        if (_kind != SaleKind.Prescription) return;
        var patients = await App.Send(new GetPatientOptions());
        if (patients.IsSuccess) _patient.DataSource = patients.Value;
        var hospitals = await App.Send(new GetHospitalOptions());
        if (hospitals.IsSuccess) _hospital.DataSource = hospitals.Value;
    }

    void AddSelected()
    {
        if (_product.SelectedItem is not CatalogItem item) { _product.FocusInput(); return; }
        var qty = int.TryParse(_qty.Text, out var q) && q > 0 ? q : 1;
        Add(item, qty);
        _product.Clear();
        _qty.Text = "1";
        _product.FocusInput();
    }

    void Add(CatalogItem item, int qty)
    {
        var line = _cart.FirstOrDefault(l => l.ProductId == item.Id);
        if (line is null)
            _cart.Add(new CartLine { ProductId = item.Id, Name = item.Name, CategoryText = item.CategoryText, ColorText = item.IsRx ? Texts.Of(item.Color) : "", Price = item.Price, Stock = item.Stock, Reimbursed = item.Reimbursed, Quantity = qty });
        else line.Quantity += qty;
        CartChanged();
    }

    void Change(int delta)
    {
        if (_cartGrid.SelectedItem is not CartLine l) return;
        l.Quantity = Math.Max(1, l.Quantity + delta);
        CartChanged();
    }

    async void CartChanged()
    {
        var selected = _cartGrid.SelectedItem;
        _cartGrid.DataSource = null;
        _cartGrid.DataSource = _cart.ToList();
        if (selected is not null && _cart.Contains(selected)) _cartGrid.SelectedItem = selected;
        await QuoteAsync();
    }

    PrescriptionColor RxColor => Colors[Math.Max(0, _color.SelectedIndex)];

    SaleDraft Draft() => new(
        _kind,
        [.. _cart.Select(l => new SaleItem(l.ProductId, l.Quantity))],
        _kind == SaleKind.Prescription ? _patient.SelectedValue as int? : null,
        _kind == SaleKind.Prescription ? _hospital.SelectedValue as int? : null,
        _kind == SaleKind.Prescription ? _branch.SelectedItem as string : null,
        RxColor,
        _kind == SaleKind.Prescription ? _rxNo.Text.Trim().ToUpperInvariant() : null,
        _kind == SaleKind.Prescription && _rxDate.Value is { } d ? DateOnly.FromDateTime(d) : null,
        _payment.SelectedIndex == 1 ? PaymentType.Card : PaymentType.Cash,
        _kind == SaleKind.Retail ? _discount.Number ?? 0 : 0,
        _seller.SelectedValue as int?);

    async Task QuoteAsync()
    {
        var version = ++_quoteVersion;
        if (_cart.Count == 0) { ShowQuote(null, null); return; }
        var draft = Draft();
        // Hasta seçilmeden reçete fiyatlanırken katılım payı varsayılan (SGK çalışan) kurala göre gösterilir
        var result = await App.Send(new QuoteSale(draft));
        if (version != _quoteVersion) return;
        ShowQuote(result.IsSuccess ? result.Value : null, result.IsSuccess ? null : string.Join(Environment.NewLine, result.Errors.Select(e => e.Message)));
    }

    void ShowQuote(SaleReceipt? q, string? error)
    {
        _lots.DataSource = q?.Lines ?? Array.Empty<ReceiptLine>();
        if (q is null)
        {
            _lines.Text = _cart.Count == 0 ? "Sepet boş." : "";
            _due.Text = _cart.Count == 0 ? "" : App.Money(_cart.Sum(l => l.Total));
            _warn.Text = error ?? (_kind == SaleKind.Prescription ? "Hasta, kurum ve reçete bilgisini girin; ürünleri ekleyin." : "Reçeteli ilaçlar serbest satışta listelenmez.");
            _warn.ForeColor = error is null ? OndaTheme.Colors.TextMuted : OndaTheme.Colors.Danger;
        }
        else
        {
            var rows = new List<string> { $"Ara toplam: {App.Money(q.Total + q.Discount)}" };
            if (q.Discount > 0) rows.Add($"İndirim: −{App.Money(q.Discount)}");
            if (q.Kind == SaleKind.Prescription)
            {
                rows.Add($"Kurum payı ({Texts.Of(q.Insurance)}): {App.Money(q.InstitutionShare)}");
                rows.Add($"Hasta katılım payı: {App.Money(q.PatientShare)}");
            }
            rows.Add($"Brüt kâr: {App.Money(q.Profit)}");
            _lines.Text = string.Join(Environment.NewLine, rows);
            _due.Text = "Tahsil: " + App.Money(q.PatientShare);
            _warn.Text = q.Kind == SaleKind.Prescription ? $"{q.PatientName ?? "Hasta seçilmedi"} · ürünler son kullanma tarihi en yakın partiden düşülür" : "Ürünler son kullanma tarihi en yakın partiden düşülür";
            _warn.ForeColor = OndaTheme.Colors.TextMuted;
        }
        DoReceiptLayout();
    }

    async Task SaveAsync()
    {
        if (_cart.Count == 0) { OndaToast.Info("Önce ürün ekleyin."); _product.FocusInput(); return; }
        if (!OndaValidate.All(this, out var bad))
        {
            bad?.FocusInput();
            OndaToast.Error("Kırmızı işaretli alanları doldurun.", "Satış yapılamadı");
            return;
        }
        _save.Busy = true;
        var result = await App.Send(new CreateSale(Draft()));
        _save.Busy = false;
        if (!result.IsSuccess) { App.ShowErrors(result, "Satış yapılamadı"); return; }
        Receipt = result.Value;
        OndaToast.Success($"Fiş {Receipt.Number} · tahsil {App.Money(Receipt.PatientShare)}", "Satış tamamlandı");
        DialogResult = DialogResult.OK;
    }

    /// <summary>Ekran görüntüsü için örnek bir reçete doldurur (kaydetmez).</summary>
    public async Task FillSampleAsync()
    {
        await LoadOptionsAsync();
        if (_kind == SaleKind.Prescription)
        {
            if (_patient.DataSource is IReadOnlyList<PatientOption> ps && ps.FirstOrDefault(p => p.Insurance == InsuranceType.Sgk && !p.ChronicReport) is { } p) _patient.SelectedValue = p.Id;
            if (_hospital.DataSource is IReadOnlyList<HospitalOption> hs && hs.FirstOrDefault() is { } h) _hospital.SelectedValue = h.Id;
            _branch.SelectedItem = Branches.All.Contains("Kardiyoloji") ? "Kardiyoloji" : Branches.All[0];
            _rxNo.Text = "3K7T2PA";
        }
        var picks = _catalog.Where(c => c.Stock > 3 && c.Price < 400 && (_kind == SaleKind.Retail || (c.IsRx && c.Color == PrescriptionColor.White))).Take(40).ToList();
        foreach (var (item, i) in picks.Where((_, i) => i % 9 == 0).Take(3).Select((x, i) => (x, i))) Add(item, i == 0 ? 2 : 1);
        await Task.Delay(300);
        await QuoteAsync();
    }

    void DoLayout()
    {
        var w = ClientSize.Width - M * 2;
        var leftW = (int)(w * 0.62);
        var rightX = M + leftW + 20;
        var rightW = w - leftW - 20;
        var y = CaptionHeight + 16;

        if (_kind == SaleKind.Prescription)
        {
            var cw = (leftW - 14) / 2;
            _patient.SetBounds(M, y, cw, _patient.Height);
            _hospital.SetBounds(M + cw + 14, y, cw, _hospital.Height);
            y = _patient.Bottom + 6;
            var c3 = (leftW - 28) / 3;
            _branch.SetBounds(M, y, c3, _branch.Height);
            _rxNo.SetBounds(M + c3 + 14, y, c3, _rxNo.Height);
            _rxDate.SetBounds(M + (c3 + 14) * 2, y, c3, _rxDate.Height);
            y = _branch.Bottom + 8;
            _color.SetBounds(M, y, Math.Min(leftW, 480), 36);
            y += 50;
        }

        _product.SetBounds(M, y, leftW - 110 - 120 - 28, _product.Height);
        _qty.SetBounds(_product.Right + 14, y, 110, _qty.Height);
        _add.SetBounds(_qty.Right + 14, y + 24, 110, 36);
        y = _product.Bottom + 10;

        var by = ClientSize.Height - 36 - 20;
        var gridBottom = by - 16 - 36 - 10;
        _cartGrid.SetBounds(M, y, leftW, Math.Max(140, gridBottom - y));
        _plus.Location = new Point(M, _cartGrid.Bottom + 10);
        _minus.Location = new Point(_plus.Right + 10, _plus.Top);
        _remove.Location = new Point(_minus.Right + 10, _plus.Top);

        var ry = CaptionHeight + 16;
        _seller.SetBounds(rightX, ry, rightW, _seller.Height);
        ry = _seller.Bottom + 8;
        _payment.SetBounds(rightX, ry, rightW, 36);
        ry += 48;
        if (_discount.Visible) { _discount.SetBounds(rightX, ry, rightW, _discount.Height); ry = _discount.Bottom + 8; }
        _receipt.SetBounds(rightX, ry, rightW, Math.Max(220, by - 16 - ry));

        _save.Location = new Point(ClientSize.Width - M - _save.Width, by);
        _cancel.Location = new Point(_save.Left - 10 - _cancel.Width, by);
        DoReceiptLayout();
    }

    void DoReceiptLayout()
    {
        var w = _receipt.Width - 32;
        var top = _receipt.ContentTop;
        _warn.MaximumSize = _lines.MaximumSize = new Size(w, 0);
        var bottom = _receipt.Height - 16;
        _warn.Location = new Point(16, bottom - _warn.PreferredHeight);
        _due.Location = new Point(14, _warn.Top - _due.PreferredHeight - 6);
        _lines.Location = new Point(16, _due.Top - _lines.PreferredHeight - 8);
        _lots.SetBounds(16, top, w, Math.Max(60, _lines.Top - 12 - top));
    }
}
