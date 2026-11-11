using Otel.Application;

namespace Otel.WinForms.Pages;

sealed class HousekeepingPage : PageBase
{
    static readonly string[] Staff = ["Hatice (1-2. kat)", "Gülsüm (3-4. kat)", "Teknik servis"];
    readonly OndaSwitch _showDone = new() { Text = "Tamamlananları da göster", Width = 240 };
    readonly OndaComboBox _staff = new() { Label = "", Placeholder = "Görevli seçin", Compact = true, Width = 220 };
    readonly OndaButton _assign = new() { Text = "Ata", Icon = "", Kind = OndaButtonKind.Outline, Width = 90 };
    readonly OndaButton _done = new() { Text = "Tamamlandı", Icon = "", Kind = OndaButtonKind.Success, Width = 150 };
    readonly OndaGrid _grid = new() { RowHeight = 36, EmptyText = "Açık görev yok, bütün odalar hazır" };

    public HousekeepingPage() : base("Kat hizmetleri", "Temizlik ve bakım görevleri")
    {
        foreach (var s in Staff) _staff.Items.Add(s);
        _grid.Columns.Add(new OndaGridColumn(nameof(TaskRow.RoomNumber), "Oda", 80));
        _grid.Columns.Add(new OndaGridColumn(nameof(TaskRow.Floor), "Kat", 60, OndaColumnKind.Number));
        _grid.Columns.Add(new OndaGridColumn(nameof(TaskRow.KindText), "Görev", 110, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(TaskRow.StateText), "Durum", 120, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(TaskRow.AssignedTo), "Görevli", 180));
        _grid.Columns.Add(new OndaGridColumn(nameof(TaskRow.Note), "Not", 220));
        _grid.Columns.Add(new OndaGridColumn(nameof(TaskRow.CreatedAt), "Açıldı", 140, OndaColumnKind.Date) { Format = "dd.MM HH:mm" });
        _grid.Columns.Add(new OndaGridColumn(nameof(TaskRow.CompletedAt), "Bitti", 140, OndaColumnKind.Date) { Format = "dd.MM HH:mm" });

        _showDone.CheckedChanged += async (_, _) => await ReloadAsync();
        _done.Click += async (_, _) => await CompleteAsync();
        _grid.RowActivated += async (_, _) => await CompleteAsync();
        _assign.Click += async (_, _) => await AssignAsync();
        Controls.AddRange(new Control[] { _showDone, _staff, _assign, _done, _grid });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new GetHousekeepingTasks(_showDone.Checked));
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        _grid.DataSource = result.Value;
        var open = result.Value.Count(t => t.StateText == "Açık");
        Subtitle = open == 0 ? "Açık görev yok" : $"{open} açık görev  ·  satıra çift tıklayın ya da Tamamlandı'ya basın; oda satışa açılır";
    }

    async Task CompleteAsync()
    {
        if (_grid.SelectedItem is not TaskRow task || task.StateText != "Açık") return;
        var r = await App.Send(new CompleteTask(task.Id));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        OndaToast.Success($"Oda {task.RoomNumber} hazır, satışa açıldı.");
        await ReloadAsync();
    }

    async Task AssignAsync()
    {
        if (_grid.SelectedItem is not TaskRow task) { OndaToast.Info("Önce bir görev seçin."); return; }
        if (_staff.SelectedItem is not string person) { OndaToast.Info("Görevli seçin."); return; }
        var r = await App.Send(new AssignTask(task.Id, person));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        await ReloadAsync();
    }

    protected override void LayoutPage(Rectangle r)
    {
        _showDone.Location = new Point(r.X, r.Y + 8);
        RightAlign(r.Right, r.Y, _staff, _assign, _done);
        _grid.SetBounds(r.X, r.Y + 54, r.Width, r.Height - 54);
    }
}
