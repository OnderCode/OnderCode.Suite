using OnderCode.Application.Results;

namespace Eczane.WinForms;

/// <summary>Kolon kısayolları: sayfalarda tekrar eden grid tanımlarını kısaltır.</summary>
static class Col
{
    public static OndaGridColumn Text(string field, string caption, int width = 140) => new(field, caption, width);
    public static OndaGridColumn Badge(string field, string caption, int width = 120) => new(field, caption, width, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor };
    public static OndaGridColumn Int(string field, string caption, int width = 90, bool total = false) => new(field, caption, width, OndaColumnKind.Number) { ShowTotal = total };
    public static OndaGridColumn Money(string field, string caption, int width = 120, bool total = false) => new(field, caption, width, OndaColumnKind.Money) { ShowTotal = total };
    public static OndaGridColumn Date(string field, string caption, int width = 110, string format = "dd.MM.yyyy") => new(field, caption, width, OndaColumnKind.Date) { Format = format };
    public static OndaGridColumn Day(string field, string caption, int width = 110) => new(field, caption, width) { Format = "dd.MM.yyyy" };
    public static OndaGridColumn Pct(string field, string caption, int width = 90) => new(field, caption, width, OndaColumnKind.Number) { Formatter = v => v is double d ? App.Percent(d) : "" };
    public static OndaGridColumn Dbl(string field, string caption, int width = 90, string format = "0.0") => new(field, caption, width, OndaColumnKind.Number) { Formatter = v => v is double d ? d.ToString(format) : "" };
    public static OndaGridColumn Progress(string field, string caption, int width = 130, double max = 1) => new(field, caption, width, OndaColumnKind.Progress) { ProgressMax = max, Formatter = v => v is double d ? "%" + (d * 100).ToString(d < 0.1 ? "0.0" : "0") : "" };
    public static OndaGridColumn Check(string field, string caption, int width = 70) => new(field, caption, width, OndaColumnKind.Check);
}

/// <summary>Alanları alt alta dizen küçük diyalog; kaydet düğmesi verilen işi çalıştırır, başarılıysa kapanır.</summary>
abstract class DialogBase : OndaForm
{
    protected const int M = 28;
    readonly List<Control[]> _rows = [];
    readonly OndaButton _cancel = new() { Action = OndaAction.Cancel, DialogResult = DialogResult.Cancel, Width = 120 };
    protected readonly OndaButton SaveButton = new() { Action = OndaAction.Save, Shortcut = Keys.F2, Width = 170 };
    readonly int _width;

    protected DialogBase(string title, string icon, int width = 560)
    {
        _width = width;
        Text = title;
        TitleIcon = icon;
        ShowMinimizeButton = ShowMaximizeButton = false;
        Resizable = false;
        CloseOnEscape = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Controls.AddRange(new Control[] { _cancel, SaveButton });
        SaveButton.Click += async (_, _) => await SaveAsync();
    }

    /// <summary>Bir satır ekler; birden fazla kontrol verilirse yan yana eşit genişlikte dizilir.</summary>
    protected void Row(params Control[] controls)
    {
        _rows.Add(controls);
        Controls.AddRange(controls);
    }

    /// <summary>Alanlar eklendikten sonra çağrılır: boyutu ve yerleşimi hesaplar.</summary>
    protected void Build()
    {
        var w = _width - M * 2;
        var y = CaptionHeight + 20;
        foreach (var row in _rows)
        {
            var visible = row.Where(c => c.Visible || !IsHandleCreated).ToArray();
            if (visible.Length == 0) continue;
            var cw = (w - (visible.Length - 1) * 14) / visible.Length;
            var h = 0;
            for (var i = 0; i < visible.Length; i++)
            {
                var c = visible[i];
                var ch = c is OndaSegmented ? 38 : c is OndaLabel l ? Math.Max(l.PreferredHeight, 22) : c.Height;
                if (c is OndaLabel lab) { lab.AutoSize = true; lab.MaximumSize = new Size(w, 0); lab.Location = new Point(M, y); ch = lab.PreferredHeight; }
                else c.SetBounds(M + i * (cw + 14), y, cw, ch);
                h = Math.Max(h, ch);
            }
            y += h + 10;
        }
        y += 14;
        ClientSize = new Size(_width, y + 36 + 22);
        var by = ClientSize.Height - 36 - 22;
        SaveButton.Location = new Point(ClientSize.Width - M - SaveButton.Width, by);
        _cancel.Location = new Point(SaveButton.Left - 10 - _cancel.Width, by);
    }

    async Task SaveAsync()
    {
        if (!OndaValidate.All(this, out var bad))
        {
            bad?.FocusInput();
            OndaToast.Error("Kırmızı işaretli alanları düzeltin.", "Kaydedilemedi");
            return;
        }
        SaveButton.Busy = true;
        try
        {
            if (await OnSaveAsync()) DialogResult = DialogResult.OK;
        }
        finally { SaveButton.Busy = false; }
    }

    /// <summary>Kaydı yapar; başarılıysa true döner (form kapanır).</summary>
    protected abstract Task<bool> OnSaveAsync();

    protected static bool Check(IResult result, string title)
    {
        if (result.IsSuccess) return true;
        App.ShowErrors(result, title);
        return false;
    }

    protected static DateOnly? DateOf(OndaDatePicker p) => p.Value is { } d ? DateOnly.FromDateTime(d) : null;
    protected static DateTime At(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);
}

/// <summary>Salt okunur ayrıntı penceresi: başlık bilgisi, isteğe bağlı grafik ve sekmeli tablolar.</summary>
sealed class DetailForm : OndaForm
{
    const int M = 24;
    readonly OndaLabel _summary = new() { Style = OndaLabelStyle.Body, AutoSize = true };
    readonly OndaSegmented _tabs = new();
    readonly OndaGrid _grid = new() { RowHeight = 32, AutoGenerateColumns = false };
    readonly OndaChart? _chart;
    readonly List<(string Name, OndaGridColumn[] Columns, object Data)> _pages = [];
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 120 };
    readonly OndaButton _close = new() { Action = OndaAction.Close, DialogResult = DialogResult.Cancel, Width = 120 };
    readonly List<Stat> _cards = [];
    readonly List<OndaButton> _footer = [];

    /// <summary>Ekran görüntüsü modu: ayarlıysa bir sonraki ayrıntı penceresi açılınca kaydedilir ve kapanır.</summary>
    public static (string Dir, string Name)? AutoCapture { get; set; }

    public DetailForm(string title, string icon, string summary, OndaChart? chart = null, Size? size = null)
    {
        Text = title;
        TitleIcon = icon;
        ShowMinimizeButton = false;
        CloseOnEscape = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = size ?? new Size(1100, 760);
        MinimumSize = new Size(900, 600);
        _summary.Text = summary;
        _chart = chart;
        Controls.AddRange(new Control[] { _summary, _tabs, _grid, _excel, _close });
        if (chart is not null) Controls.Add(chart);
        _tabs.SelectedIndexChanged += (_, _) => ShowTab();
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog(Text);
        Resize += (_, _) => DoLayout();
    }

    public DetailForm Card(string title, decimal value, string prefix = "", string? note = null, string icon = "", Color? color = null, int decimals = 0)
    {
        var c = new Stat { Title = title, Value = value, Prefix = prefix, DeltaText = note ?? "", Icon = icon, Decimals = decimals };
        if (color is { } col) c.IconColor = col;
        _cards.Add(c);
        Controls.Add(c);
        return this;
    }

    /// <summary>Alt bilgi satırına (Excel'in soluna) bir düğme ekler.</summary>
    public DetailForm AddFooterButton(OndaButton button)
    {
        _footer.Add(button);
        Controls.Add(button);
        return this;
    }

    public DetailForm Tab(string name, object data, params OndaGridColumn[] columns)
    {
        _pages.Add((name, columns, data));
        return this;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _tabs.Items = [.. _pages.Select(p => p.Name)];
        _tabs.Visible = _pages.Count > 1;
        _tabs.SelectedIndex = 0;
        ShowTab();
        DoLayout();
        foreach (var c in _cards) c.Replay();
        _chart?.Replay();
        if (AutoCapture is { } cap)
        {
            AutoCapture = null;
            BeginInvoke(new Action(async () =>
            {
                await Task.Delay(1200);
                ScreenCapture.Capture(this, cap.Dir, cap.Name);
                Close();
            }));
        }
    }

    void ShowTab()
    {
        if (_pages.Count == 0) return;
        var p = _pages[Math.Max(0, _tabs.SelectedIndex)];
        _grid.DataSource = null;
        _grid.Columns.Clear();
        _grid.Columns.AddRange(p.Columns);
        _grid.DataSource = p.Data;
    }

    void DoLayout()
    {
        var w = ClientSize.Width - M * 2;
        var y = CaptionHeight + 16;
        _summary.MaximumSize = new Size(w, 0);
        _summary.Location = new Point(M, y);
        y += _summary.PreferredHeight + 12;
        if (_cards.Count > 0)
        {
            var cw = (w - (_cards.Count - 1) * 14) / _cards.Count;
            for (var i = 0; i < _cards.Count; i++) _cards[i].SetBounds(M + i * (cw + 14), y, cw, 134);
            y += 134 + 14;
        }
        if (_chart is not null) { _chart.SetBounds(M, y, w, 220); y += 220 + 14; }
        if (_tabs.Visible) { _tabs.SetBounds(M, y, Math.Min(w, 160 * _pages.Count), 38); y += 50; }
        var by = ClientSize.Height - 36 - 18;
        _grid.SetBounds(M, y, w, Math.Max(120, by - 14 - y));
        _close.Location = new Point(ClientSize.Width - M - _close.Width, by);
        _excel.Location = new Point(_close.Left - 10 - _excel.Width, by);
        var x = M;
        foreach (var b in _footer) { b.Location = new Point(x, by); x += b.Width + 10; }
    }
}

/// <summary>Tek alanlı küçük giriş penceresi (sayı ya da metin).</summary>
sealed class InputForm : DialogBase
{
    readonly OndaTextBox _box;
    readonly Func<string, Task<bool>> _save;

    InputForm(string title, string icon, string label, string? value, string? hint, OndaInputKind kind, Func<string, Task<bool>> save) : base(title, icon, 480)
    {
        _save = save;
        _box = new OndaTextBox { Label = label, Required = true, Kind = kind, Text = value ?? "", HelperText = hint ?? "" };
        SaveButton.Text = "Tamam";
        SaveButton.Action = OndaAction.Ok;
        Row(_box);
        Build();
    }

    protected override Task<bool> OnSaveAsync() => _save(_box.Text.Trim());

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _box.FocusInput();
    }

    public static bool Ask(IWin32Window? owner, string title, string icon, string label, string? value, string? hint, OndaInputKind kind, Func<string, Task<bool>> save)
    {
        using var f = new InputForm(title, icon, label, value, hint, kind, save);
        return f.ShowDialog(owner) == DialogResult.OK;
    }
}

/// <summary>Değişim yüzdesi olmayan kartlarda da açıklama satırını (DeltaText) gösteren istatistik kartı.</summary>
sealed class Stat : OndaStatCard
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Delta is not null || string.IsNullOrEmpty(DeltaText) || DeltaText == "geçen aya göre") return;
        var w = Sparkline is { Length: > 1 } ? (int)(Width * 0.56f) - 18 : Width - 36;
        Gfx.Text(e.Graphics, DeltaText, OndaTheme.FontSmall, new Rectangle(18, Height - 36, w, 22), OndaTheme.Colors.TextMuted);
    }
}
