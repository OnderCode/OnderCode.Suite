using Donanim.WinForms.Controls;

namespace Donanim.WinForms.Pages;

/// <summary>Pil ve sistem: pil sağlığı, Windows sürümü ve açık kalma süresi, UEFI/Güvenli Önyükleme/TPM, ağ bağdaştırıcıları, tarama notları.</summary>
sealed class SystemPage : KarnePage
{
    readonly BatteryTile _battery = new();
    readonly InfoList _os = new() { Title = "Windows", Icon = Glyph.System, IconColor = App.Cyan, LabelWidth = 160 };
    readonly InfoList _security = new() { Title = "Güvenlik ve Windows 11 uygunluğu", Icon = Glyph.Shield, IconColor = App.Teal, LabelWidth = 200 };
    readonly OndaLabel _netTitle = new() { Text = "Ağ bağdaştırıcıları", Style = OndaLabelStyle.Title };
    readonly OndaGrid _net = new() { ShowFilterRow = false, ShowFooter = false, RowHeight = 34, AutoGenerateColumns = false, EmptyText = "Ağ bağdaştırıcısı bulunamadı" };
    readonly TextTile _notes = new() { Title = "Tarama notları", Icon = Glyph.Info, IconColor = App.Slate };

    sealed record NetRow(string Name, string Kind, string Speed, string State);

    public SystemPage() : base("Pil ve sistem", "Yükleniyor…")
    {
        _net.Columns.AddRange(
        [
            new OndaGridColumn(nameof(NetRow.Name), "Bağdaştırıcı", 320),
            new OndaGridColumn(nameof(NetRow.Kind), "Tür", 110, OndaColumnKind.Badge) { BadgeColor = v => v as string == "Kablosuz" ? App.Violet : OndaTheme.Colors.Primary },
            new OndaGridColumn(nameof(NetRow.Speed), "Bağlantı hızı", 130),
            new OndaGridColumn(nameof(NetRow.State), "Durum", 120, OndaColumnKind.Badge) { BadgeColor = v => v as string == "Bağlı" ? OndaTheme.Colors.Success : App.Slate },
        ]);
        Controls.AddRange(new Control[] { _battery, _os, _security, _netTitle, _net, _notes });
    }

    protected override void Bind(Karne k)
    {
        var s = k.Snapshot;
        var now = s.IsSample ? s.CapturedAt : DateTime.Now;
        Subtitle = $"{MachineLine(s)}  ·  {s.Os.Name}{(s.Battery is { DesignWh: > 0 } b ? $"  ·  pil sağlığı {Formats.Percent(b.Health)}" : "")}";
        _battery.SetData(s.Battery, s.Machine.IsLaptop);
        var up = s.Os.Uptime(now);
        _os.SetRows(
        [
            ("Sürüm", s.Os.Name, s.Os.IsWindows10 ? (Color?)OndaTheme.Colors.Warning : null),
            ("Yapı", $"{s.Os.Version} · {s.Os.Architecture}", null),
            ("Kurulum", s.Os.InstallDate is { } i ? $"{i:dd.MM.yyyy} ({(int)(now - i).TotalDays} gün önce)" : "-", null),
            ("Açık kalma süresi", up is { } u ? Formats.Duration(u) : "-", up is { TotalDays: >= 7 } ? (Color?)OndaTheme.Colors.Warning : null),
            ("Destek", s.Os.IsWindows11 ? "Güncellemeler sürüyor" : s.Os.IsWindows10 ? "Ücretsiz destek bitti (14.10.2025)" : "-", s.Os.IsWindows11 ? OndaTheme.Colors.Success : OndaTheme.Colors.Warning),
        ]);
        Color? Yes(bool? v) => v switch { true => OndaTheme.Colors.Success, false => OndaTheme.Colors.Warning, _ => null };
        _security.SetRows(
        [
            ("Önyükleme", s.Security.Uefi switch { true => "UEFI", false => "Eski BIOS (Legacy)", _ => "Bilinmiyor" }, Yes(s.Security.Uefi)),
            ("Güvenli Önyükleme", Formats.YesNo(s.Security.SecureBoot), Yes(s.Security.SecureBoot)),
            ("TPM", s.Security.TpmVersion is { } t ? $"TPM {t}" : "Bilinmiyor (yönetici olarak açın)", s.Security.TpmVersion is { } t2 ? (Color?)(t2.StartsWith('2') ? OndaTheme.Colors.Success : OndaTheme.Colors.Warning) : null),
            ("Sanallaştırma", Formats.YesNo(s.Cpu.Virtualization), Yes(s.Cpu.Virtualization)),
            ("64 bit", s.Os.Architecture.Contains("64") ? "Evet" : "Hayır", null),
        ]);
        _net.DataSource = s.Network.Select(n => new NetRow(n.Name, n.Kind, n.SpeedMbps is { } sp ? (sp >= 1000 ? $"{sp / 1000.0:0.#} Gb/sn" : $"{sp} Mb/sn") : "-", n.Connected ? "Bağlı" : "Bağlı değil")).ToList();
        var notes = new List<string>();
        if (s.IsSample) notes.Add("Bu kurgusal bir örnek bilgisayardır; kendi bilgisayarınız için Karne sayfasında \"Bu bilgisayar\"ı seçin.");
        notes.AddRange(s.Warnings);
        notes.Add("Okunmayanlar: seri numarası, MAC adresi, kullanıcı ve dosya bilgileri. Uygulama internete bağlanmaz.");
        _notes.Body = string.Join(Environment.NewLine, notes.Select(n => "•  " + n));
    }

    protected override void LayoutPage(Rectangle r)
    {
        var half = (r.Width - Gap) / 2;
        var top = Math.Max(_os.PreferredHeight, 230);
        _battery.SetBounds(r.X, r.Y, half, top);
        _os.SetBounds(r.X + half + Gap, r.Y, r.Width - half - Gap, top);
        var y = r.Y + top + Gap;
        var sh = _security.PreferredHeight;
        _security.SetBounds(r.X, y, half, sh);
        _notes.SetBounds(r.X + half + Gap, y, r.Width - half - Gap, sh);
        y += sh + Gap;
        _netTitle.Location = new Point(r.X - 2, y + 2);
        _net.SetBounds(r.X, y + 36, r.Width, Math.Max(110, r.Bottom - y - 36));
    }
}
