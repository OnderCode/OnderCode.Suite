namespace Otel.WinForms.Pages;

/// <summary>Sayfa iskeleti: başlık, alt başlık, içerik yerleşimi ve veri yenileme.</summary>
abstract class PageBase : OndaPanel
{
    protected const int M = 28, Gap = 20, ContentY = 100;
    readonly OndaLabel _title = new() { Style = OndaLabelStyle.Header };
    readonly OndaLabel _sub = new() { Style = OndaLabelStyle.Muted };
    bool _loading;

    protected PageBase(string title, string subtitle)
    {
        Dock = DockStyle.Fill;
        _title.Text = title;
        _sub.Text = subtitle;
        _title.Location = new Point(M - 3, 22);
        _sub.Location = new Point(M - 1, 62);
        Controls.Add(_title);
        Controls.Add(_sub);
        Resize += (_, _) => Relayout();
    }

    protected string Subtitle { set => _sub.Text = value; }

    protected void Relayout()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        LayoutPage(new Rectangle(M, ContentY, ClientSize.Width - M * 2, ClientSize.Height - ContentY - M));
    }

    protected abstract void LayoutPage(Rectangle r);

    /// <summary>Verileri uygulama katmanından yeniden çeker.</summary>
    public async Task ReloadAsync()
    {
        if (_loading) return;
        _loading = true;
        try { await LoadAsync(); }
        finally { _loading = false; }
    }

    protected abstract Task LoadAsync();

    /// <summary>Sayfa her gösterildiğinde güncel veriyle açılır.</summary>
    public virtual void OnShown()
    {
        Relayout();
        _ = ReloadAsync();
    }

    protected static void RightAlign(int right, int y, params Control[] items)
    {
        var x = right;
        for (var i = items.Length - 1; i >= 0; i--)
        {
            x -= items[i].Width;
            items[i].Location = new Point(x, y);
            x -= 10;
        }
    }
}
