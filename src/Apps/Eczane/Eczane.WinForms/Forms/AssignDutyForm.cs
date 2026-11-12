namespace Eczane.WinForms;

/// <summary>Bir nöbet gününe nöbetçi eczacı ve isteğe bağlı yardımcı personel atar. İzinli personel listede yine görünür ama kayıt reddedilir.</summary>
sealed class AssignDutyForm : DialogBase
{
    readonly DutyCell _cell;
    readonly OndaLabel _info = new() { Style = OndaLabelStyle.Body };
    readonly OndaComboBox _pharmacist = new() { Label = "Nöbetçi eczacı", Required = true, DisplayMember = nameof(DutyStat.Name), ValueMember = nameof(DutyStat.EmployeeId) };
    readonly OndaComboBox _assistant = new() { Label = "Yardımcı personel", DisplayMember = nameof(DutyStat.Name), ValueMember = nameof(DutyStat.EmployeeId), Placeholder = "Yok", ShowClearButton = true };

    public static bool Open(IWin32Window? owner, DutyCell cell, IReadOnlyList<DutyStat> stats)
    {
        using var f = new AssignDutyForm(cell, stats);
        return f.ShowDialog(owner) == DialogResult.OK;
    }

    AssignDutyForm(DutyCell cell, IReadOnlyList<DutyStat> stats) : base($"Nöbet · {cell.Date:dd MMMM yyyy dddd}", "\uE787", 560)
    {
        _cell = cell;
        var pharmacists = stats.Where(s => s.Role is StaffRole.ResponsiblePharmacist or StaffRole.Pharmacist).ToList();
        var others = stats.Where(s => s.Role is not (StaffRole.ResponsiblePharmacist or StaffRole.Pharmacist)).ToList();
        _pharmacist.DataSource = pharmacists;
        _assistant.DataSource = others;
        _pharmacist.SelectedValue = cell.PharmacistId;
        _assistant.SelectedValue = cell.AssistantId;
        _info.Text = $"{cell.KindText} nöbeti  ·  puan: gece 1, pazar 2, bayram 3" +
                     (cell.OnLeave.Count > 0 ? $"\nO gün izinli: {string.Join(", ", cell.OnLeave)}" : "") +
                     "\nBu yıl: " + string.Join("  ·  ", stats.Select(s => $"{s.Name.Split(' ')[0]} {s.YearCount} nöbet/{s.YearPoints} puan"));
        SaveButton.Text = "Atamayı kaydet";
        Row(_info);
        Row(_pharmacist);
        Row(_assistant);
        Build();
    }

    protected override async Task<bool> OnSaveAsync()
    {
        if (_cell.ShiftId is not int shift || _pharmacist.SelectedValue is not int ph) return false;
        var r = await App.Send(new AssignDuty(shift, ph, _assistant.SelectedValue as int?));
        if (!Check(r, "Atama yapılamadı")) return false;
        OndaToast.Success($"{_cell.Date:dd.MM} nöbeti atandı.");
        return true;
    }
}
