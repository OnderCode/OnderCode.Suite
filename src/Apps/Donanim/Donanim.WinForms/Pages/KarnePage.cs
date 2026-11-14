namespace Donanim.WinForms.Pages;

/// <summary>Bütün sayfaların tabanı: güncel karneyi alır, yoksa bekler.</summary>
abstract class KarnePage(string title, string subtitle) : PageBase(title, subtitle)
{
    protected override Task LoadAsync()
    {
        if (App.Karne is { } k)
        {
            Bind(k);
            Relayout();
            foreach (var t in Controls.OfType<Controls.AnimatedTile>()) t.Replay();
            foreach (var host in Controls.OfType<Panel>())
                foreach (var t in host.Controls.OfType<Controls.AnimatedTile>()) t.Replay();
        }
        else Subtitle = App.Scanning ? "Donanım taranıyor…" : "Henüz tarama yapılmadı";
        return Task.CompletedTask;
    }

    protected abstract void Bind(Karne k);

    protected static string MachineLine(HardwareSnapshot s) =>
        s.IsSample ? $"{s.Machine.DisplayName} · kurgusal örnek" : $"{s.Machine.DisplayName} · {s.CapturedAt:dd.MM.yyyy HH:mm}";

    /// <summary>Kaydırılabilir iç alan.</summary>
    protected static OndaPanel ScrollHost() => new() { AutoScroll = true };
}
