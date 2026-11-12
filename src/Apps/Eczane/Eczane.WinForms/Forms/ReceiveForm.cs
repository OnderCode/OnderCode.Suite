using System.Globalization;

namespace Eczane.WinForms;

/// <summary>Sipariş teslim alma: her kalem için parti numarası ve son kullanma tarihi girilir (depo irsaliyesinden); stok partileri oluşur, cari borç yazılır.</summary>
sealed class ReceiveForm : OndaForm
{
    const int M = 24;

    sealed class Line(OrderLineRow row)
    {
        public int LineId { get; } = row.LineId;
        public string Product { get; } = row.Product;
        public int Quantity { get; } = row.Quantity;
        public string FreeText { get; } = row.FreeText;
        public bool ColdChain { get; } = row.ColdChain;
        public string LotNo { get; set; } = row.LotNo;
        public string Expiry { get; set; } = row.Expiry.ToString("dd.MM.yyyy");
    }

    readonly int _orderId;
    readonly OndaLabel _head = new() { Style = OndaLabelStyle.Body, AutoSize = true };
    readonly OndaGrid _grid = new() { RowHeight = 34, ShowFilterRow = false, AutoGenerateColumns = false, AllowEdit = true };
    readonly OndaButton _cancel = new() { Action = OndaAction.Cancel, DialogResult = DialogResult.Cancel, Width = 120 };
    readonly OndaButton _save = new() { Action = OndaAction.Save, Text = "Teslim al ve stoka gir", Shortcut = Keys.F2, Width = 230 };
    List<Line> _lines = [];

    ReceiveForm(int orderId)
    {
        _orderId = orderId;
        Text = "Sipariş teslim alma";
        TitleIcon = "\uE7B8";
        ShowMinimizeButton = false;
        CloseOnEscape = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 660);
        _grid.Columns.AddRange(
        [
            new OndaGridColumn(nameof(Line.Product), "Ürün", 300) { ReadOnly = true },
            new OndaGridColumn(nameof(Line.Quantity), "Adet", 70, OndaColumnKind.Number) { ReadOnly = true, ShowTotal = true },
            new OndaGridColumn(nameof(Line.FreeText), "MF", 60) { ReadOnly = true },
            new OndaGridColumn(nameof(Line.ColdChain), "Soğuk", 70, OndaColumnKind.Check) { ReadOnly = true },
            new OndaGridColumn(nameof(Line.LotNo), "Parti no ✎", 150),
            new OndaGridColumn(nameof(Line.Expiry), "SKT ✎ (gg.aa.yyyy)", 170),
        ]);
        _grid.CellValidating += (_, e) =>
        {
            if (e.Column.Field == nameof(Line.Expiry) && !DateOnly.TryParseExact(e.NewValue as string, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly _))
            { e.Cancel = true; e.Error = "Tarihi gg.aa.yyyy biçiminde yazın."; }
        };
        Controls.AddRange(new Control[] { _head, _grid, _cancel, _save });
        _save.Click += async (_, _) => await SaveAsync();
        Resize += (_, _) => DoLayout();
    }

    public static async Task<bool> OpenAsync(IWin32Window? owner, int orderId)
    {
        using var f = new ReceiveForm(orderId);
        if (!await f.LoadAsync()) return false;
        return f.ShowDialog(owner) == DialogResult.OK;
    }

    async Task<bool> LoadAsync()
    {
        var r = await App.Send(new GetOrder(_orderId));
        if (!r.IsSuccess) { App.ShowErrors(r); return false; }
        var o = r.Value.Order;
        _head.Text = $"{o.Number}  ·  {o.Wholesaler}  ·  {o.Lines} kalem, {o.Boxes} kutu, {App.Money(o.Total)}\nİrsaliyedeki parti numarası ve son kullanma tarihini kontrol edin; hücreye çift tıklayarak düzeltebilirsiniz. Soğuk zincir ürünleri hemen dolaba kaldırın.";
        _lines = [.. r.Value.Lines.Select(l => new Line(l))];
        _grid.DataSource = _lines;
        DoLayout();
        return true;
    }

    async Task SaveAsync()
    {
        var lots = new List<ReceivedLot>();
        foreach (var l in _lines)
        {
            if (string.IsNullOrWhiteSpace(l.LotNo) || !DateOnly.TryParseExact(l.Expiry, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp))
            {
                OndaToast.Error($"{l.Product}: parti no ve SKT (gg.aa.yyyy) girin.", "Eksik bilgi");
                return;
            }
            lots.Add(new ReceivedLot(l.LineId, l.LotNo.Trim().ToUpperInvariant(), exp));
        }
        _save.Busy = true;
        var r = await App.Send(new ReceiveOrder(_orderId, lots));
        _save.Busy = false;
        if (!r.IsSuccess) { App.ShowErrors(r, "Teslim alınamadı"); return; }
        OndaToast.Success($"{r.Value} parti stoka girdi.", "Sipariş teslim alındı");
        DialogResult = DialogResult.OK;
    }

    void DoLayout()
    {
        var w = ClientSize.Width - M * 2;
        var y = CaptionHeight + 16;
        _head.MaximumSize = new Size(w, 0);
        _head.Location = new Point(M, y);
        y += _head.PreferredHeight + 12;
        var by = ClientSize.Height - 36 - 18;
        _grid.SetBounds(M, y, w, by - 14 - y);
        _save.Location = new Point(ClientSize.Width - M - _save.Width, by);
        _cancel.Location = new Point(_save.Left - 10 - _cancel.Width, by);
    }
}
