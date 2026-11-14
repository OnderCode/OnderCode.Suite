using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using Donanim.WinForms.Pages;

namespace Donanim.WinForms;

/// <summary>
/// Tanıtım videosu (--video klasör): uygulamayı kurgusal örnek bilgisayarlarla gezer, pencerenin karelerini
/// zaman damgasıyla JPEG olarak kaydeder ve üzerine sanal bir fare imleci çizer.
/// Kareler ffmpeg ile videoya çevrilir (frames.txt, concat biçiminde).
/// </summary>
static class VideoScript
{
    public static async Task Run(ShellForm shell, string dir)
    {
        Directory.CreateDirectory(dir);
        shell.WindowState = FormWindowState.Normal;
        shell.Bounds = new Rectangle(10, 10, 1440, 900);
        using var rec = new Recorder(shell, dir);
        try
        {
            await Task.Delay(400);
            rec.Start();
            var loading = App.LoadAsync(Samples.OfficeLaptopName);
            await rec.Wait(600);
            await loading;
            var overview = (OverviewPage)shell.ShowPage("karne");
            await rec.Wait(1400);
            await rec.MoveTo(overview, new Point(300, 220), 900);
            await rec.Wait(700);
            for (var i = 0; i < 3; i++)
            {
                var t = overview.GradeTileAt(i);
                await rec.MoveTo(t, new Point(t.Width / 2, t.Height / 2), 500);
                await rec.Wait(350);
            }
            await rec.MoveTo(overview, new Point(500, 640), 800);
            await rec.Wait(1400);

            var memory = (MemoryPage)await rec.Menu("memory");
            await rec.Wait(1200);
            await rec.HoverSlot(memory, 1, 1800);
            await rec.HoverSlot(memory, 0, 1600);

            await rec.Menu("storage");
            await rec.Wait(800);
            await rec.MoveTo(shell, new Point(560, 470), 700);
            await rec.Wait(1600);

            await rec.Menu("system");
            await rec.Wait(2600);

            var advice = await rec.Menu("advice");
            await rec.Wait(1200);
            var host = advice.Controls.OfType<OndaPanel>().FirstOrDefault(p => p.AutoScroll);
            await rec.MoveTo(advice, new Point(400, 420), 600);
            if (host is not null) await rec.Scroll(host, 600, 2200);
            await rec.Wait(800);
            if (host is not null) await rec.Scroll(host, 0, 900);

            overview = (OverviewPage)await rec.Menu("karne");
            await rec.Wait(700);
            var seg = overview.SourceSelector;
            await rec.MoveTo(seg, new Point(seg.Width * 5 / 6, seg.Height / 2), 900);
            rec.Click();
            await rec.Wait(150);
            var load2 = App.LoadAsync(Samples.GamingDesktopName);
            await rec.Wait(400);
            await load2;
            await rec.Wait(2400);

            memory = (MemoryPage)await rec.Menu("memory");
            await rec.Wait(1200);
            await rec.HoverSlot(memory, 0, 1500);
            await rec.HoverSlot(memory, 1, 2000);

            await rec.Menu("cpu");
            await rec.Wait(3600);

            await rec.Menu("graphics");
            await rec.Wait(2000);

            var toggle = shell.ThemeToggleRect;
            await rec.MoveTo(shell.SideMenu, new Point(toggle.X + toggle.Width / 2, toggle.Y + toggle.Height / 2), 1000);
            rec.Click();
            OndaTheme.Toggle();
            await rec.Wait(1100);
            await rec.Menu("karne");
            await rec.Wait(2600);
            await rec.Menu("memory");
            await rec.Wait(1000);
            await rec.HoverSlot(memory, 3, 1800);
            await rec.Wait(600);
            rec.Stop();
            rec.WriteIndex();
            OndaTheme.Apply(OndaThemeMode.Light);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "hata.txt"), ex.ToString());
        }
        shell.Close();
    }

    sealed class Recorder(ShellForm shell, string dir) : IDisposable
    {
        readonly System.Windows.Forms.Timer _timer = new() { Interval = 55 };
        readonly Stopwatch _clock = new();
        readonly List<(string File, long Ms)> _frames = [];
        readonly ImageCodecInfo _jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
        PointF _cursor = new(720, 450);
        long _clickAt = -10_000;
        bool _busy;

        public void Start()
        {
            _timer.Tick += (_, _) => Capture();
            _clock.Start();
            _timer.Start();
            Capture();
        }

        public void Stop()
        {
            Capture();
            _timer.Stop();
            _clock.Stop();
        }

        void Capture()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var ms = _clock.ElapsedMilliseconds;
                using var bmp = new Bitmap(shell.Width, shell.Height);
                shell.DrawToBitmap(bmp, new Rectangle(Point.Empty, shell.Size));
                using (var g = Graphics.FromImage(bmp)) DrawCursor(g, ms);
                var name = $"f{_frames.Count:00000}.jpg";
                using var p = new EncoderParameters(1);
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                bmp.Save(Path.Combine(dir, name), _jpeg, p);
                _frames.Add((name, ms));
            }
            finally { _busy = false; }
        }

        void DrawCursor(Graphics g, long ms)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var since = ms - _clickAt;
            if (since is >= 0 and < 500)
            {
                var t = since / 500f;
                var r = 10 + 26 * t;
                using var pen = new Pen(Color.FromArgb((int)(200 * (1 - t)), OndaTheme.Colors.Primary), 3);
                g.DrawEllipse(pen, _cursor.X - r, _cursor.Y - r, r * 2, r * 2);
            }
            var x = _cursor.X;
            var y = _cursor.Y;
            PointF[] arrow =
            [
                new(x, y), new(x, y + 21), new(x + 5, y + 16), new(x + 9, y + 25), new(x + 12.5f, y + 23.5f), new(x + 8.5f, y + 15), new(x + 15, y + 15),
            ];
            using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                g.FillPolygon(shadow, arrow.Select(pt => new PointF(pt.X + 1.5f, pt.Y + 2)).ToArray());
            g.FillPolygon(Brushes.White, arrow);
            using var outline = new Pen(Color.FromArgb(20, 20, 24), 1.4f) { LineJoin = LineJoin.Round };
            g.DrawPolygon(outline, arrow);
        }

        Point WindowPoint(Control c, Point local)
        {
            var screen = c.PointToScreen(local);
            return new Point(screen.X - shell.Left, screen.Y - shell.Top);
        }

        public async Task Wait(int ms) => await Task.Delay(ms);

        public async Task MoveTo(Control c, Point local, int ms)
        {
            var from = _cursor;
            var to = (PointF)WindowPoint(c, local);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                var t = (float)sw.ElapsedMilliseconds / ms;
                t = t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
                _cursor = new PointF(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
                await Task.Delay(12);
            }
            _cursor = to;
        }

        public void Click() => _clickAt = _clock.ElapsedMilliseconds;

        public async Task<KarnePage> Menu(string key)
        {
            var r = shell.MenuItemRect(key);
            await MoveTo(shell.SideMenu, new Point(r.X + 60, r.Y + r.Height / 2), 650);
            Click();
            await Task.Delay(120);
            var page = shell.ShowPage(key);
            await page.ReloadAsync();
            return page;
        }

        public async Task HoverSlot(MemoryPage page, int index, int hold)
        {
            var rect = page.Board.SlotRect(index);
            if (rect.IsEmpty) return;
            await MoveTo(page.Board, new Point((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2)), 650);
            page.Board.SetHover(index);
            await Task.Delay(hold);
        }

        public async Task Scroll(OndaPanel host, int y, int ms)
        {
            var from = -host.AutoScrollPosition.Y;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                var t = Math.Min(1f, (float)sw.ElapsedMilliseconds / ms);
                t = t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;
                host.AutoScrollPosition = new Point(0, (int)(from + (y - from) * t));
                await Task.Delay(16);
            }
            host.AutoScrollPosition = new Point(0, y);
        }

        public void WriteIndex()
        {
            var sb = new StringBuilder("ffconcat version 1.0\n");
            for (var i = 0; i < _frames.Count; i++)
            {
                sb.Append("file ").Append(_frames[i].File).Append('\n');
                var dur = i + 1 < _frames.Count ? _frames[i + 1].Ms - _frames[i].Ms : 100;
                sb.Append("duration ").Append((dur / 1000.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            }
            sb.Append("file ").Append(_frames[^1].File).Append('\n');
            File.WriteAllText(Path.Combine(dir, "frames.txt"), sb.ToString());
            File.WriteAllText(Path.Combine(dir, "bitti.txt"), $"{_frames.Count} kare, {_clock.ElapsedMilliseconds / 1000.0:0.0} sn");
        }

        public void Dispose() => _timer.Dispose();
    }
}
