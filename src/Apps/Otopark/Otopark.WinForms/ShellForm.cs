using Otopark.WinForms.Pages;

namespace Otopark.WinForms;

/// <summary>Uygulama kabuğu: yan menü + sayfalar. F2 araç girişi, F3 çıkış, F4 yıkama siparişi.</summary>
sealed class ShellForm : OndaForm
{
    readonly OndaSideMenu _menu = new()
    {
        Title = "OnderCode Otopark",
        Subtitle = "Otopark ve araç yıkama",
        UserName = Environment.UserName,
        UserRole = "Gişe",
    };
    readonly OndaPanel _host = new() { Dock = DockStyle.Fill };
    readonly Dictionary<string, PageBase> _pages = [];

    public ShellForm()
    {
        Text = "OnderCode Otopark";
        TitleIcon = "";
        Size = new Size(1440, 900);
        MinimumSize = new Size(1240, 780);
        KeyPreview = true;

        var dash = new OndaMenuItem("Gösterge paneli", "", "dash");
        _menu.Items.Add(OndaMenuItem.Header("Gişe"));
        _menu.Items.Add(dash);
        _menu.Items.Add(new OndaMenuItem("Otopark haritası", "", "lot"));
        _menu.Items.Add(new OndaMenuItem("Araç hareketleri", "", "sessions"));
        _menu.Items.Add(OndaMenuItem.Header("Yıkama"));
        _menu.Items.Add(new OndaMenuItem("Yıkama kuyruğu", "", "wash"));
        _menu.Items.Add(OndaMenuItem.Header("Yönetim"));
        _menu.Items.Add(new OndaMenuItem("Abonelikler", "", "subs"));
        _menu.Items.Add(new OndaMenuItem("Raporlar", "", "reports"));
        _menu.Items.Add(new OndaMenuItem("Hakkında", "", "about"));

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
                "OnderCode Otopark: otopark gişesi, araç yıkama kuyruğu, abonelik ve raporlama. Bütün plakalar ve kişiler örnek veridir.\n\n" +
                "Tarife: ilk 15 dakika ücretsiz, ilk saat ve ek saat ücreti, günlük üst sınır. Yıkama yaptırana ilk 2 saat otopark ücretsiz.\n\n" +
                "OnderCode Suite: .NET 10, katmanlı mimari, EF Core + SQLite (kurulumsuz), Onda arayüz bileşenleri.\n" +
                "Açık kaynak, MIT lisanslı.\n\nAbdurrahman Önder · ondercode.com", "Hakkında");
            return;
        }
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "lot" => new LotPage(),
                "sessions" => new SessionsPage(),
                "wash" => new WashPage(),
                "subs" => new SubscriptionsPage(),
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

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Program.ShotsDir is not null) return;
        switch (e.KeyCode)
        {
            case Keys.F2:
                e.Handled = true;
                if (EntryForm.Open(this) is not null) RefreshVisiblePage();
                break;
            case Keys.F3:
                e.Handled = true;
                if (await ExitForm.OpenByPlateAsync(this)) RefreshVisiblePage();
                break;
            case Keys.F4:
                e.Handled = true;
                if (WashOrderForm.Open(this) is not null) RefreshVisiblePage();
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
