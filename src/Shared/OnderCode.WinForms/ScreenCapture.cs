using System.Drawing.Imaging;

namespace OnderCode.WinForms;

/// <summary>README ve web sitesi için ekran görüntüsü yardımcıları ("--shots klasör" modu).</summary>
public static class ScreenCapture
{
    /// <summary>Sayfayı yeniler, animasyonların bitmesini bekler ve formun görüntüsünü kaydeder.</summary>
    public static async Task SnapAsync(Form form, string dir, string name, PageBase? page = null, int delay = 1500)
    {
        if (page is not null) await page.ReloadAsync();
        await Task.Delay(delay);
        Capture(form, dir, name);
    }

    public static void Capture(Control target, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        target.Refresh();
        using var bmp = new Bitmap(target.Width, target.Height);
        target.DrawToBitmap(bmp, new Rectangle(Point.Empty, target.Size));
        bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
    }

    /// <summary>"--shots klasör" bağımsız değişkenini okur.</summary>
    public static string? ShotsDirectory(string[] args)
    {
        var i = Array.IndexOf(args, "--shots");
        return i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : null;
    }
}
