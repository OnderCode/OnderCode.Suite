using Otel.WinForms.Pages;

namespace Otel.WinForms;

/// <summary>Uygulama kabuğu: yan menü + sayfalar.</summary>
sealed class ShellForm : OndaForm
{
    readonly OndaSideMenu _menu = new()
    {
        Title = "Lale Otel",
        Subtitle = "Otel Otomasyonu",
        UserName = Environment.UserName,
        UserRole = "Ön büro",
    };
    readonly OndaPanel _host = new() { Dock = DockStyle.Fill };
    readonly Dictionary<string, PageBase> _pages = [];

    public ShellForm()
    {
        Text = "Lale Otel · OnderCode Suite";
        TitleIcon = "";
        Size = new Size(1440, 900);
        MinimumSize = new Size(1240, 780);

        var dash = new OndaMenuItem("Gösterge paneli", "", "dash");
        _menu.Items.Add(OndaMenuItem.Header("Ön büro"));
        _menu.Items.Add(dash);
        _menu.Items.Add(new OndaMenuItem("Oda haritası", "", "rooms"));
        _menu.Items.Add(new OndaMenuItem("Rezervasyon çizelgesi", "", "timeline"));
        _menu.Items.Add(new OndaMenuItem("Rezervasyonlar", "", "res"));
        _menu.Items.Add(new OndaMenuItem("Misafirler", "", "guests"));
        _menu.Items.Add(OndaMenuItem.Header("Operasyon"));
        _menu.Items.Add(new OndaMenuItem("Kat hizmetleri", "", "hk"));
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
                "Lale Otel kurgusal bir oteldir; bütün kişiler ve rezervasyonlar örnek veridir.\n\n" +
                "OnderCode Suite: .NET 10, katmanlı mimari, EF Core + SQLite (kurulumsuz), Onda arayüz bileşenleri.\n" +
                "Açık kaynak, MIT lisanslı.\n\nAbdurrahman Önder · ondercode.com", "Hakkında");
            return;
        }
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "rooms" => new RoomMapPage(),
                "timeline" => new TimelinePage(),
                "res" => new ReservationsPage(),
                "guests" => new GuestsPage(),
                "hk" => new HousekeepingPage(),
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

    /// <summary>Herhangi bir ekranda veri değiştiğinde açık sayfaları tazeler.</summary>
    public void RefreshVisiblePage()
    {
        foreach (var p in _pages.Values.Where(p => p.Visible)) _ = p.ReloadAsync();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try { Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath); } catch { }
        if (Program.ShotsDir is not null) Screenshots.Run(this, Program.ShotsDir);
    }
}
