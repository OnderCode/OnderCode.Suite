using Eczane.WinForms.Pages;

namespace Eczane.WinForms;

/// <summary>Uygulama kabuğu: yan menü + sayfalar. F1 hakkında, F2 reçeteli satış, F3 serbest satış, F4 ilaç listesi.</summary>
sealed class ShellForm : OndaForm
{
    readonly OndaSideMenu _menu = new()
    {
        Title = "OnderCode Eczane",
        Subtitle = "Eczane yönetim sistemi",
        UserName = Environment.UserName,
        UserRole = "Eczacı",
    };
    readonly OndaPanel _host = new() { Dock = DockStyle.Fill };
    readonly Dictionary<string, PageBase> _pages = [];

    public ShellForm()
    {
        Text = "OnderCode Eczane";
        TitleIcon = "\uE95E";
        Size = new Size(1440, 900);
        MinimumSize = new Size(1240, 780);
        KeyPreview = true;

        var dash = new OndaMenuItem("Gösterge paneli", "\uE80F", "dash");
        _menu.Items.Add(dash);
        _menu.Items.Add(new OndaMenuItem("Satışlar", "\uE7BF", "sales"));
        _menu.Items.Add(new OndaMenuItem("Hastalar", "\uE716", "patients"));
        _menu.Items.Add(new OndaMenuItem("Hastaneler", "\uE95E", "hospitals"));
        _menu.Items.Add(OndaMenuItem.Header("Stok"));
        _menu.Items.Add(new OndaMenuItem("İlaçlar ve stok", "\uE8F1", "products"));
        _menu.Items.Add(new OndaMenuItem("Miad takibi", "\uE823", "expiry"));
        _menu.Items.Add(new OndaMenuItem("Sipariş önerisi", "\uE8EF", "reorder"));
        _menu.Items.Add(new OndaMenuItem("Siparişler", "\uE7B8", "orders"));
        _menu.Items.Add(new OndaMenuItem("Ecza depoları", "\uE825", "wholesalers"));
        _menu.Items.Add(OndaMenuItem.Header("Eczane"));
        _menu.Items.Add(new OndaMenuItem("Personel ve izin", "\uE77B", "staff"));
        _menu.Items.Add(new OndaMenuItem("Nöbet çizelgesi", "\uE787", "duty"));
        _menu.Items.Add(new OndaMenuItem("Demirbaş", "\uE772", "assets"));
        _menu.Items.Add(new OndaMenuItem("Kurum faturaları", "\uE8C7", "invoices"));
        _menu.Items.Add(new OndaMenuItem("Raporlar", "\uE9F9", "reports"));

        Controls.Add(_host);
        Controls.Add(_menu);
        _menu.SelectedChanged += (_, _) => Navigate(_menu.SelectedItem?.Tag as string);
        _menu.SelectedItem = dash;
    }

    public PageBase ShowPage(string key)
    {
        var item = _menu.Items.First(i => i.Tag as string == key);
        _menu.SelectedItem = item;
        return _pages[key];
    }

    void Navigate(string? key)
    {
        if (key is null) return;
        if (key == "about")
        {
            OndaMessageBox.Info(this,
                "OnderCode Eczane: reçeteli ve serbest satış, ilaç ve parti (SKT) takibi, ecza deposu siparişleri ve cari hesap, hasta geçmişi, hastane analizi, personel ve izin, nöbet çizelgesi, demirbaş ve amortisman, SGK ve özel sigorta faturaları, raporlar.\n\n" +
                "Kişisel veri tutmaz: T.C. kimlik numarası, telefon ve adres alanı yoktur. Bütün ilaç, hasta, hastane ve depo adları örnek veridir.\n\n" +
                "OnderCode Suite: .NET 10, katmanlı mimari, EF Core + SQLite (kurulumsuz), Onda arayüz bileşenleri.\n" +
                "Açık kaynak, MIT lisanslı.\n\nAbdurrahman Önder · ondercode.com", "Hakkında");
            return;
        }
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "sales" => new SalesPage(),
                "patients" => new PatientsPage(),
                "hospitals" => new HospitalsPage(),
                "products" => new ProductsPage(),
                "expiry" => new ExpiryPage(),
                "reorder" => new ReorderPage(),
                "orders" => new OrdersPage(),
                "wholesalers" => new WholesalersPage(),
                "staff" => new StaffPage(),
                "duty" => new DutyPage(),
                "assets" => new AssetsPage(),
                "invoices" => new InvoicesPage(),
                "reports" => new ReportsPage(),
                _ => new DashboardPage(),
            };
            page.Visible = false;
            _pages[key] = page;
            _host.Controls.Add(page);
        }
        _host.SuspendLayout();
        foreach (var p in _pages.Values) p.Visible = p == page;
        _host.ResumeLayout();
        page.OnShown();
    }

    /// <summary>Bir işlemden sonra açık sayfayı tazeler.</summary>
    public void RefreshVisiblePage()
    {
        foreach (var p in _pages.Values.Where(p => p.Visible)) _ = p.ReloadAsync();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Program.ShotsDir is not null) return;
        switch (e.KeyCode)
        {
            case Keys.F2:
                e.Handled = true;
                if (SaleForm.Open(this, SaleKind.Prescription) is not null) RefreshVisiblePage();
                break;
            case Keys.F3:
                e.Handled = true;
                if (SaleForm.Open(this, SaleKind.Retail) is not null) RefreshVisiblePage();
                break;
            case Keys.F4:
                e.Handled = true;
                ShowPage("products");
                break;
            case Keys.F1:
                e.Handled = true;
                Navigate("about");
                break;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try { Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath); } catch { }
        if (Program.ShotsDir is not null) Screenshots.Run(this, Program.ShotsDir);
    }
}
