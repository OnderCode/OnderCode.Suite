using Eczane.WinForms.Controls;

namespace Eczane.WinForms.Pages;

/// <summary>
/// Nöbet çizelgesi: ay takvimi, otomatik adil planlama (yıl boyu puan dengesi, iki nöbet arası en az iki gün, izinliler atlanır),
/// elle atama, nöbet günü ekleme/kaldırma ve personel bazında nöbet istatistiği.
/// </summary>
sealed class DutyPage : PageBase
{
    readonly OndaButton _prev = new() { Icon = "\uE76B", Kind = OndaButtonKind.Outline, Width = 44 };
    readonly OndaLabel _title = new() { Style = OndaLabelStyle.Title, AutoSize = true };
    readonly OndaButton _next = new() { Icon = "\uE76C", Kind = OndaButtonKind.Outline, Width = 44 };
    readonly OndaButton _plan = new() { Text = "Otomatik planla", Icon = "\uE72C", Width = 180 };
    readonly OndaButton _assign = new() { Text = "Ata", Icon = "\uE77B", Kind = OndaButtonKind.Outline, Width = 100 };
    readonly OndaButton _add = new() { Text = "Nöbet günü ekle", Icon = "\uE710", Kind = OndaButtonKind.Outline, Width = 170 };
    readonly OndaButton _remove = new() { Text = "Kaldır", Icon = "\uE74D", Kind = OndaButtonKind.Outline, Width = 110 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 100 };
    readonly DutyCalendar _calendar = new();
    readonly OndaLabel _statsTitle = new() { Text = "Nöbet dağılımı", Style = OndaLabelStyle.Title };
    readonly OndaGrid _stats = new() { RowHeight = 32, ShowFilterRow = false, AutoGenerateColumns = false };
    readonly OndaGrid _list = new() { Visible = false, AutoGenerateColumns = false };
    DutyMonth? _month;
    DateOnly _current;

    public DutyPage() : base("Nöbet çizelgesi", "Yükleniyor…")
    {
        var today = App.Today;
        _current = new DateOnly(today.Year, today.Month, 1);
        _stats.Columns.AddRange(
        [
            Col.Text(nameof(DutyStat.Name), "Personel", 118),
            Col.Int(nameof(DutyStat.MonthCount), "Ay", 48, true),
            Col.Int(nameof(DutyStat.YearCount), "Yıl", 45, true),
            Col.Int(nameof(DutyStat.YearPoints), "Puan", 50),
            Col.Int(nameof(DutyStat.Sundays), "Pz/bayram", 72),
        ]);
        _list.Columns.AddRange(
        [
            Col.Day(nameof(DutyCell.Date), "Tarih", 110),
            Col.Badge(nameof(DutyCell.KindText), "Tür", 90),
            Col.Text(nameof(DutyCell.Pharmacist), "Nöbetçi eczacı", 180),
            Col.Text(nameof(DutyCell.Assistant), "Yardımcı", 180),
            Col.Text(nameof(DutyCell.Note), "Not", 200),
        ]);
        _prev.Click += async (_, _) => { _current = _current.AddMonths(-1); await ReloadAsync(); };
        _next.Click += async (_, _) => { _current = _current.AddMonths(1); await ReloadAsync(); };
        _plan.Click += async (_, _) => await PlanAsync();
        _assign.Click += async (_, _) => { if (_calendar.Selected is { } c) await AssignAsync(c); else OndaToast.Info("Takvimden bir gün seçin."); };
        _add.Click += async (_, _) => await AddAsync();
        _remove.Click += async (_, _) => await RemoveAsync();
        _excel.Click += (_, _) => _list.ExportExcelWithDialog($"Nöbet çizelgesi {_month?.Title}");
        _calendar.DayActivated += async (_, c) => { if (c.IsDuty) await AssignAsync(c); else await AddAsync(); };
        Controls.AddRange(new Control[] { _prev, _title, _next, _plan, _assign, _add, _remove, _excel, _calendar, _statsTitle, _stats, _list });
    }

    public DutyCalendar Calendar => _calendar;

    protected override async Task LoadAsync()
    {
        var r = await App.Send(new GetDutyMonth(_current.Year, _current.Month));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        _month = r.Value;
        _calendar.SetMonth(_month);
        _stats.DataSource = _month.Stats;
        _list.DataSource = _month.Days.Where(d => d.IsDuty).ToList();
        _title.Text = _month.Title;
        Subtitle = $"{_month.Duties} nöbet günü  ·  {(_month.Unstaffed == 0 ? "hepsi atanmış" : $"{_month.Unstaffed} nöbet atanmayı bekliyor")}  ·  gece 1, pazar 2, bayram 3 puan; dağıtım yıl boyu puan dengesine göre  ·  güne çift tıklayın";
        Relayout();
    }

    async Task PlanAsync()
    {
        if (_month is null) return;
        var onlyEmpty = true;
        if (_month.Unstaffed == 0)
        {
            if (!OndaMessageBox.Ask(FindForm(), "Bu ayın bütün nöbetleri atanmış. Bugünden sonraki nöbetler baştan dağıtılsın mı?", "Yeniden planla")) return;
            onlyEmpty = false;
        }
        var r = await App.Send(new PlanDutyMonth(_current.Year, _current.Month, onlyEmpty));
        if (!r.IsSuccess) { App.ShowErrors(r, "Planlanamadı"); return; }
        OndaToast.Success($"{r.Value} nöbete atama yapıldı.", "Nöbetler planlandı");
        await ReloadAsync();
    }

    async Task AssignAsync(DutyCell c)
    {
        if (_month is null || !c.IsDuty) { OndaToast.Info("Seçili gün nöbet günü değil."); return; }
        if (AssignDutyForm.Open(FindForm(), c, _month.Stats)) await ReloadAsync();
    }

    async Task AddAsync()
    {
        var date = _calendar.Selected?.Date ?? App.Today;
        if (_calendar.Selected is { IsDuty: true }) { OndaToast.Info("Seçili gün zaten nöbet günü."); return; }
        var kind = date.DayOfWeek == DayOfWeek.Sunday ? DutyKind.Sunday : DutyKind.Night;
        if (!OndaMessageBox.Ask(FindForm(), $"{date:dd MMMM yyyy dddd} {Texts.Of(kind).ToLower(Texts.Turkish)} nöbeti olarak eklensin mi?", "Nöbet günü")) return;
        var r = await App.Send(new AddDutyDay(date, kind));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        await ReloadAsync();
        _calendar.Select(date);
    }

    async Task RemoveAsync()
    {
        if (_calendar.Selected is not { ShiftId: int id } c) { OndaToast.Info("Takvimden bir nöbet günü seçin."); return; }
        if (!OndaMessageBox.Ask(FindForm(), $"{c.Date:dd.MM.yyyy} nöbet günü kaldırılsın mı?", "Nöbet günü")) return;
        var r = await App.Send(new RemoveDutyDay(id));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        await ReloadAsync();
    }

    protected override void LayoutPage(Rectangle r)
    {
        _prev.Location = new Point(r.X, r.Y);
        _title.Location = new Point(_prev.Right + 12, r.Y + 6);
        _next.Location = new Point(_prev.Right + 12 + Math.Max(150, _title.PreferredWidth) + 12, r.Y);
        RightAlign(r.Right, r.Y, _excel, _remove, _add, _assign, _plan);
        var y = r.Y + 54;
        const int sideW = 340;
        _calendar.SetBounds(r.X, y, r.Width - sideW - Gap, r.Bottom - y);
        var sx = r.Right - sideW;
        _statsTitle.Location = new Point(sx - 2, y);
        _stats.SetBounds(sx, y + 36, sideW, r.Bottom - y - 36);
    }
}
