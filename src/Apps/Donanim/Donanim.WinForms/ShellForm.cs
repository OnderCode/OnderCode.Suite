using Donanim.WinForms.Pages;

namespace Donanim.WinForms;

/// <summary>Uygulama kabuğu: yan menü + sayfalar. F5 yeniden tarar.</summary>
sealed class ShellForm : OndaForm
{
    readonly OndaSideMenu _menu = new()
    {
        Title = "OnderCode Donanım",
        Subtitle = "Donanım karnesi",
        UserName = "Bu bilgisayar",
        UserRole = "Taranıyor…",
        ExpandedWidth = 272,
    };
    readonly OndaPanel _host = new() { Dock = DockStyle.Fill };
    readonly Dictionary<string, KarnePage> _pages = [];
    readonly string _startSource;

    public ShellForm(string startSource)
    {
        _startSource = startSource;
        Text = "OnderCode Donanım";
        TitleIcon = Glyph.Karne;
        Size = new Size(1440, 900);
        MinimumSize = new Size(1240, 780);
        KeyPreview = true;

        var karne = new OndaMenuItem("Karne", Glyph.Karne, "karne");
        _menu.Items.Add(karne);
        _menu.Items.Add(OndaMenuItem.Header("Bileşenler"));
        _menu.Items.Add(new OndaMenuItem("Bellek (RAM)", Glyph.Memory, "memory"));
        _menu.Items.Add(new OndaMenuItem("İşlemci ve anakart", Glyph.Cpu, "cpu"));
        _menu.Items.Add(new OndaMenuItem("Depolama", Glyph.Disk, "storage"));
        _menu.Items.Add(new OndaMenuItem("Ekran kartı ve ekran", Glyph.Monitor, "graphics"));
        _menu.Items.Add(new OndaMenuItem("Pil ve sistem", Glyph.Battery, "system"));
        _menu.Items.Add(OndaMenuItem.Header("Sonuç"));
        _menu.Items.Add(new OndaMenuItem("Öneriler ve rapor", Glyph.Bulb, "advice"));

        _menu.Width = _menu.ExpandedWidth;
        Controls.Add(_host);
        Controls.Add(_menu);
        _menu.SelectedChanged += (_, _) => Navigate(_menu.SelectedItem?.Tag as string);
        _menu.SelectedItem = karne;

        App.ScanStarted += (_, _) =>
        {
            _menu.UserName = App.Source == App.ThisComputer ? "Bu bilgisayar" : App.Source.Replace("Örnek: ", "");
            _menu.UserRole = "Taranıyor…";
            _menu.Invalidate();
            foreach (var p in _pages.Values.Where(p => p.Visible)) _ = p.ReloadAsync();
        };
        App.Changed += (_, _) =>
        {
            if (App.Karne is { } k)
            {
                _menu.UserRole = k.Snapshot.IsSample ? "Kurgusal örnek" : $"Tarandı · {k.Snapshot.CapturedAt:HH:mm}";
                var memory = _menu.Items.First(i => i.Tag as string == "memory");
                memory.Badge = k.Snapshot.Memory.FreeSlots > 0 ? $"{k.Snapshot.Memory.FreeSlots} boş" : null;
                var advice = _menu.Items.First(i => i.Tag as string == "advice");
                advice.Badge = k.CriticalCount + k.RecommendedCount > 0 ? (k.CriticalCount + k.RecommendedCount).ToString() : null;
                _menu.Invalidate();
            }
            foreach (var p in _pages.Values.Where(p => p.Visible)) _ = p.ReloadAsync();
        };
    }

    public OndaSideMenu SideMenu => _menu;

    public KarnePage ShowPage(string key)
    {
        _menu.SelectedItem = _menu.Items.First(i => i.Tag as string == key);
        return _pages[key];
    }

    public T Page<T>(string key) where T : KarnePage => (T)ShowPage(key);

    /// <summary>Menü öğesinin menü içindeki dikdörtgeni (video imleci için).</summary>
    public Rectangle MenuItemRect(string key)
    {
        int y = 72 + 8;
        foreach (var it in _menu.Items)
        {
            var h = it.IsHeader ? 32 : 42;
            if (it.Tag as string == key) return new Rectangle(10, y, _menu.Width - 20, h);
            y += h + (it.IsHeader ? 0 : 2);
        }
        return Rectangle.Empty;
    }

    public Rectangle ThemeToggleRect => new(_menu.Width - 48, _menu.Height - 68 + 18, 34, 34);

    void Navigate(string? key)
    {
        if (key is null) return;
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "memory" => new MemoryPage(),
                "cpu" => new ProcessorPage(),
                "storage" => new StoragePage(),
                "graphics" => new GraphicsPage(),
                "system" => new SystemPage(),
                "advice" => new AdvicePage(),
                _ => CreateOverview(),
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

    OverviewPage CreateOverview()
    {
        var o = new OverviewPage();
        o.GradeClicked += (_, cat) => ShowPage(cat switch
        {
            GradeCategory.Processor => "cpu",
            GradeCategory.Memory => "memory",
            GradeCategory.Storage => "storage",
            GradeCategory.Graphics => "graphics",
            _ => "system",
        });
        return o;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Program.Automated) return;
        if (e.KeyCode == Keys.F5)
        {
            e.Handled = true;
            _ = App.LoadAsync(App.Source);
        }
        else if (e.KeyCode == Keys.F1)
        {
            e.Handled = true;
            OndaMessageBox.Info(this,
                "OnderCode Donanım: bilgisayarın donanım karnesi. İşlemci, bellek yuvaları, diskler, ekran kartı, pil ve Windows bilgisi; her bileşene 1–5 not, somut yükseltme önerileri ve alışveriş listesi, PDF/Excel rapor, ikinci el ilanı metni.\n\n" +
                "Bilgiler Windows'un WMI arayüzünden okunur; uygulama internete bağlanmaz, seri numarası ve MAC adresi okumaz.\n\n" +
                "OnderCode Suite · .NET 10 · Onda arayüz bileşenleri · MIT lisansı\nAbdurrahman Önder · ondercode.com", "Hakkında");
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try { Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath); } catch (Exception) { }
        if (Program.ShotsDir is { } shots) { Screenshots.Run(this, shots); return; }
        if (Program.VideoDir is { } video) { await VideoScript.Run(this, video); return; }
        if (Program.ScanDir is { } scan) { Screenshots.RunReal(this, scan); return; }
        await App.LoadAsync(_startSource);
    }
}
