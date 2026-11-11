using System.Drawing.Imaging;
using Otel.Application;
using Otel.Domain;
using Otel.WinForms.Pages;

namespace Otel.WinForms;

/// <summary>README ve web sitesi için ekran görüntüleri (--shots klasör). Her çalıştırmada taze demo verisi kullanılır.</summary>
static class Screenshots
{
    public static async void Run(ShellForm shell, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            shell.WindowState = FormWindowState.Normal;
            shell.Bounds = new Rectangle(10, 10, 1440, 900);

            await Snap(shell, dir, "01-gosterge-paneli", shell.ShowPage("dash"));

            var rooms = (RoomMapPage)shell.ShowPage("rooms");
            await rooms.ReloadAsync();
            var board = await App.Send(new GetRoomBoard());
            if (board.IsSuccess && board.Value.FirstOrDefault(t => t.Status == RoomStatus.Occupied) is { } occupied) rooms.Map.Select(occupied.RoomId);
            await Snap(shell, dir, "02-oda-haritasi", null);

            await Snap(shell, dir, "03-rezervasyon-cizelgesi", shell.ShowPage("timeline"));
            await Snap(shell, dir, "04-rezervasyonlar", shell.ShowPage("res"));
            await Snap(shell, dir, "05-misafirler", shell.ShowPage("guests"));
            await Snap(shell, dir, "06-kat-hizmetleri", shell.ShowPage("hk"));

            using (var form = new ReservationForm(checkIn: App.Today.AddDays(35)) { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60) })
            {
                form.Show(shell);
                await Task.Delay(600);
                await form.FillSampleAsync();
                await Task.Delay(500);
                Capture(form, dir, "07-yeni-rezervasyon");
                form.Close();
            }

            if (board.IsSuccess && board.Value.FirstOrDefault(t => t.ReservationId is not null) is { ReservationId: int resId })
            {
                using var detail = new ReservationDetailForm(resId) { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60) };
                await detail.LoadAsync();
                detail.Show(shell);
                await Task.Delay(700);
                Capture(detail, dir, "08-folyo");
                detail.Close();
            }

            OndaTheme.Apply(OndaThemeMode.Dark);
            await Snap(shell, dir, "09-koyu-tema", shell.ShowPage("rooms"));
            await Snap(shell, dir, "10-koyu-gosterge", shell.ShowPage("dash"));
            OndaTheme.Apply(OndaThemeMode.Light);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "hata.txt"), ex.ToString());
        }
        shell.Close();
    }

    static async Task Snap(Form form, string dir, string name, PageBase? page)
    {
        if (page is not null) await page.ReloadAsync();
        await Task.Delay(1500); // sayaç ve grafik animasyonları bitsin
        Capture(form, dir, name);
    }

    static void Capture(Control target, string dir, string name)
    {
        target.Refresh();
        using var bmp = new Bitmap(target.Width, target.Height);
        target.DrawToBitmap(bmp, new Rectangle(Point.Empty, target.Size));
        bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
    }
}
