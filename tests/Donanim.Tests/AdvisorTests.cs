using Donanim.Core;

namespace Donanim.Tests;

public sealed class AdvisorTests
{
    static readonly DateTime Now = new(2026, 11, 14, 16, 0, 0);

    [Fact]
    public void Office_Laptop_Gets_Memory_Ssd_Battery_And_Windows_Advice()
    {
        var k = Advisor.Evaluate(Samples.OfficeLaptop(Now), Now);
        Assert.Equal(6, k.Grades.Count);
        Assert.Contains(k.Advice, a => a.Category == GradeCategory.Memory && a.Title.Contains("Boş yuvaya 8 GB DDR4-2666 SO-DIMM"));
        Assert.Contains(k.Advice, a => a.Priority == AdvicePriority.Critical && a.Title.Contains("SSD"));
        Assert.Contains(k.Advice, a => a.Category == GradeCategory.Battery && a.Priority == AdvicePriority.Recommended);
        Assert.Contains(k.Advice, a => a.Title.Contains("Windows 10"));
        Assert.Contains(k.Advice, a => a.Title.Contains("gündür yeniden başlatılmadı"));
        Assert.Contains(k.Shopping, s => s.Part.Contains("SSD"));
        Assert.Contains(k.Shopping, s => s.Part.Contains("8 GB DDR4-2666 SO-DIMM"));
        Assert.Equal(2, k.Grades.Single(g => g.Category == GradeCategory.Storage).Score);
        Assert.InRange(k.Average, 2, 3.5);
        // Kritikler önce gelir
        Assert.Equal(AdvicePriority.Critical, k.Advice[0].Priority);
    }

    [Fact]
    public void Gaming_Desktop_Gets_Xmp_And_Full_Drive_Advice()
    {
        var k = Advisor.Evaluate(Samples.GamingDesktop(Now), Now);
        Assert.Equal(5, k.Grades.Count); // pil yok
        Assert.DoesNotContain(k.Grades, g => g.Category == GradeCategory.Battery);
        Assert.Contains(k.Advice, a => a.Title.Contains("XMP"));
        Assert.Contains(k.Advice, a => a.Title.StartsWith("D: sürücüsü neredeyse dolu"));
        Assert.DoesNotContain(k.Advice, a => a.Title.Contains("SSD'ye"));
        Assert.Equal(5, k.Grades.Single(g => g.Category == GradeCategory.Memory).Score);
        Assert.Equal(5, k.Grades.Single(g => g.Category == GradeCategory.Processor).Score);
        Assert.True(k.Average >= 4.5);
    }

    [Fact]
    public void Memory_Model_Knows_Slots_And_Channels()
    {
        var m = Samples.OfficeLaptop(Now).Memory;
        Assert.Equal(2, m.SlotCount);
        Assert.Equal(1, m.FreeSlots);
        Assert.True(m.IsSingleChannel);
        Assert.Equal(MemoryType.Ddr4, m.Type);
        Assert.Equal(2400, m.ConfiguredMts);
        var g = Samples.GamingDesktop(Now).Memory;
        Assert.False(g.IsSingleChannel);
        Assert.Equal(2, g.FreeSlots);
        Assert.False(g.IsMixed);
    }

    [Fact]
    public void Full_Slots_With_Little_Ram_Suggest_Bigger_Kit()
    {
        var s = Samples.OfficeLaptop(Now) with
        {
            Memory = new MemoryInfo(
                [
                    new MemorySlot("A", "", new MemoryModule("X", "P", 4, MemoryType.Ddr4, MemoryFormFactor.SoDimm, 2400, 2400)),
                    new MemorySlot("B", "", new MemoryModule("X", "P", 4, MemoryType.Ddr4, MemoryFormFactor.SoDimm, 2400, 2400)),
                ], 16, 8),
        };
        var k = Advisor.Evaluate(s, Now);
        var a = Assert.Single(k.Advice, x => x.Category == GradeCategory.Memory && x.Title.Contains("kit"));
        Assert.Contains("2 × 8 GB", a.Detail);
        Assert.Contains(k.Shopping, x => x.Quantity == 2 && x.Part.StartsWith("8 GB"));
    }

    [Fact]
    public void Unhealthy_Disk_Is_Critical_And_Lowers_Grade()
    {
        var s = Samples.GamingDesktop(Now);
        var bad = s.Disks[1] with { Health = DiskHealth.Warning };
        s = s with { Disks = [s.Disks[0], bad] };
        var k = Advisor.Evaluate(s, Now);
        Assert.Contains(k.Advice, a => a.Priority == AdvicePriority.Critical && a.Title.Contains("yedek"));
        Assert.Equal(1, k.Grades.Single(g => g.Category == GradeCategory.Storage).Score);
    }

    [Fact]
    public void Healthy_Machine_Gets_Positive_Message()
    {
        var s = Samples.GamingDesktop(Now);
        var disks = s.Disks.Select(d => d with { Volumes = d.Volumes.Select(v => v with { FreeGb = v.SizeGb / 2 }).ToList() }).ToList();
        var slots = s.Memory.Slots.Select(x => x.Module is { } m ? x with { Module = m with { ConfiguredMts = 3600 } } : x).ToList();
        s = s with { Disks = disks, Memory = s.Memory with { Slots = slots } };
        var k = Advisor.Evaluate(s, Now);
        Assert.Contains(k.Advice, a => a.Priority == AdvicePriority.Good);
        Assert.Empty(k.Shopping);
    }

    [Theory]
    [InlineData(5, "Pekiyi")]
    [InlineData(4, "İyi")]
    [InlineData(3, "Orta")]
    [InlineData(2, "Geçer")]
    [InlineData(1, "Zayıf")]
    public void Grade_Labels_Follow_Turkish_Report_Card(int score, string label) => Assert.Equal(label, Karne.Label(score));

    [Fact]
    public void Battery_Wear_And_Formats()
    {
        var b = new BatteryInfo(50, 35, null, null, null);
        Assert.Equal(0.3, b.Wear, 3);
        Assert.Equal("1 TB", Formats.Gb(1000));
        Assert.Equal("476 GB", Formats.Gb(476));
        Assert.Equal("8 GB", Formats.Gb(7.96));
        Assert.Equal("1,9 TB", Formats.Gb(1863));
    }

    [Fact]
    public void Report_And_Listing_Are_Built()
    {
        var k = Advisor.Evaluate(Samples.OfficeLaptop(Now), Now);
        var doc = KarneReport.Build(k);
        Assert.Equal("Donanım karnesi", doc.Title);
        Assert.Contains(doc.Tables, t => t.Title == "Bellek yuvaları" && t.Rows.Count == 2);
        Assert.Contains(doc.Tables, t => t.Title == "Alışveriş listesi");
        var listing = KarneReport.Listing(k);
        Assert.Contains("Intel Core i5-8250U", listing);
        Assert.Contains("Pil sağlığı", listing);
        Assert.DoesNotContain("OFIS-DIZUSTU", listing); // bilgisayar adı ilana girmez
        var pdf = OnderCode.Reporting.Export.ReportExporters.ToBytes(doc, OnderCode.Reporting.Export.ReportExporters.Pdf);
        Assert.True(pdf.Length > 2000);
    }
}
