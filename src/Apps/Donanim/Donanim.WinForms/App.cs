using Donanim.Windows;

namespace Donanim.WinForms;

/// <summary>Uygulama durumu: seçili kaynak (bu bilgisayar ya da örnek), son tarama ve karne.</summary>
static class App
{
    public const string ThisComputer = "Bu bilgisayar";

    public static string Source { get; private set; } = ThisComputer;
    public static Karne? Karne { get; private set; }
    public static HardwareSnapshot? Snapshot => Karne?.Snapshot;
    public static bool Scanning { get; private set; }

    /// <summary>Yeni karne hazır olduğunda (UI iş parçacığında) tetiklenir.</summary>
    public static event EventHandler? Changed;
    public static event EventHandler? ScanStarted;

    public static IReadOnlyList<string> Sources { get; } = [ThisComputer, .. Samples.Names];

    public static async Task LoadAsync(string source)
    {
        if (Scanning) return;
        Scanning = true;
        Source = source;
        ScanStarted?.Invoke(null, EventArgs.Empty);
        try
        {
            var now = DateTime.Now;
            var snap = source == ThisComputer
                ? await Task.Run(() => WmiReader.Read(now))
                : Samples.Get(source, now);
            if (source != ThisComputer) await Task.Delay(250);
            Karne = Advisor.Evaluate(snap, now);
        }
        finally { Scanning = false; }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    // ---------------- görsel kurallar ----------------

    public static readonly Color Violet = Color.FromArgb(139, 92, 246);
    public static readonly Color Cyan = Color.FromArgb(6, 182, 212);
    public static readonly Color Teal = Color.FromArgb(20, 184, 166);
    public static readonly Color Amber = Color.FromArgb(245, 158, 11);
    public static readonly Color Pink = Color.FromArgb(236, 72, 153);
    public static readonly Color Slate = Color.FromArgb(148, 163, 184);
    public static readonly Color Lime = Color.FromArgb(132, 204, 22);

    /// <summary>Karne notunun rengi: 5 yeşil … 1 kırmızı.</summary>
    public static Color GradeColor(double score) => score switch
    {
        >= 4.5 => OndaTheme.Colors.Success,
        >= 3.5 => Lime,
        >= 2.5 => Amber,
        >= 1.5 => Color.FromArgb(249, 115, 22),
        _ => OndaTheme.Colors.Danger,
    };

    public static Color PriorityColor(AdvicePriority p) => p switch
    {
        AdvicePriority.Critical => OndaTheme.Colors.Danger,
        AdvicePriority.Recommended => OndaTheme.Colors.Warning,
        AdvicePriority.Info => OndaTheme.Colors.Info,
        _ => OndaTheme.Colors.Success,
    };

    public static string PriorityIcon(AdvicePriority p) => p switch
    {
        AdvicePriority.Critical => Glyph.Warning,
        AdvicePriority.Recommended => Glyph.Bulb,
        AdvicePriority.Info => Glyph.Info,
        _ => Glyph.Check,
    };

    public static string CategoryIcon(GradeCategory c) => c switch
    {
        GradeCategory.Processor => Glyph.Cpu,
        GradeCategory.Memory => Glyph.Memory,
        GradeCategory.Storage => Glyph.Disk,
        GradeCategory.Graphics => Glyph.Monitor,
        GradeCategory.Battery => Glyph.Battery,
        _ => Glyph.System,
    };

    public static Color CategoryColor(GradeCategory c) => c switch
    {
        GradeCategory.Processor => OndaTheme.Colors.Primary,
        GradeCategory.Memory => Violet,
        GradeCategory.Storage => Teal,
        GradeCategory.Graphics => Pink,
        GradeCategory.Battery => Amber,
        _ => Cyan,
    };

    public static Color DiskColor(DiskKind k) => k switch
    {
        DiskKind.NvmeSsd => Violet,
        DiskKind.SataSsd => Teal,
        DiskKind.Hdd => Amber,
        DiskKind.Usb => Slate,
        _ => Slate,
    };
}

/// <summary>Segoe MDL2 / Fluent simgeleri.</summary>
static class Glyph
{
    public const string Karne = "";
    public const string Cpu = "";
    public const string Memory = "";
    public const string Disk = "";
    public const string Monitor = "";
    public const string Battery = "";
    public const string System = "";
    public const string Bulb = "";
    public const string Cart = "";
    public const string Laptop = "";
    public const string Desktop = "";
    public const string Refresh = "";
    public const string Document = "";
    public const string Copy = "";
    public const string Warning = "";
    public const string Info = "";
    public const string Check = "";
    public const string Network = "";
    public const string Shield = "";
    public const string Speed = "";
    public const string Clock = "";
}
