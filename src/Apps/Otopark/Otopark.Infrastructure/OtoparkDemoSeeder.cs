using Microsoft.EntityFrameworkCore;
using OnderCode.Data.Initialization;
using Otopark.Application;
using Otopark.Domain;

namespace Otopark.Infrastructure;

/// <summary>
/// Örnek veri: 74 park yeri, 5 yıkama paketi, 28 abone ve son 63 günün araç hareketleri. Hareketler bugünün saatine göre
/// üretilir; uygulama hangi saatte açılırsa açılsın içeride araç, kuyrukta yıkama vardır.
/// Talep deseni gerçekçidir: hafta içi iş merkezi yoğunluğu (sabah 8-10, öğle, akşam 17-19), hafta sonu yıkama artışı.
/// Plakalar rastgele üretilir; kişi adları uydurmadır.
/// </summary>
public sealed class OtoparkDemoSeeder(TimeProvider time) : ISeeder<OtoparkDbContext>
{
    private const int HistoryDays = 63;

    private static readonly string[] FirstNames =
    [
        "Ayşe", "Mehmet", "Zeynep", "Ahmet", "Elif", "Mustafa", "Fatma", "Emre", "Selin", "Burak", "Deniz", "Can",
        "Merve", "Kerem", "Ece", "Oğuz", "Derya", "Serkan", "İrem", "Tolga", "Gizem", "Barış", "Şule", "Onur",
    ];

    private static readonly string[] LastNames =
    [
        "Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Yıldız", "Öztürk", "Aydın", "Arslan", "Doğan", "Kılıç", "Aslan",
        "Çetin", "Koç", "Kurt", "Özdemir", "Polat", "Güneş", "Bulut", "Aksoy", "Ünal", "Tekin",
    ];

    private static readonly string[] Washers = ["Murat", "Ercan", "Yusuf", "Sinan"];
    private const string PlateLetters = "ABCDEFGHJKLMNPRSTUVYZ";

    public int Order => 0;

    public async Task SeedAsync(OtoparkDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Spots.AnyAsync(cancellationToken)) return;

        var now = time.Now();
        var today = DateOnly.FromDateTime(now);
        var rnd = new Random(2026);

        // ---- Park yerleri ----
        var spots = new List<ParkingSpot>();
        for (var i = 1; i <= 20; i++)
            spots.Add(new ParkingSpot($"A-{i:00}", "A", i <= 2 ? SpotKind.Disabled : i >= 19 ? SpotKind.Electric : SpotKind.Standard));
        for (var i = 1; i <= 36; i++)
            spots.Add(new ParkingSpot($"B-{i:00}", "B", i >= 35 ? SpotKind.Electric : SpotKind.Standard));
        for (var i = 1; i <= 10; i++) spots.Add(new ParkingSpot($"C-{i:00}", "C", SpotKind.Large));
        for (var i = 1; i <= 8; i++) spots.Add(new ParkingSpot($"M-{i:00}", "M", SpotKind.Motorcycle));
        var serviceSpot = spots.First(s => s.Code == "B-17");
        db.Spots.AddRange(spots);

        // ---- Yıkama paketleri ----
        var outside = new WashPackage("DIS", "Dış yıkama", 20, 250m, 350m, 150m, 1);
        var full = new WashPackage("ICD", "İç-dış yıkama", 40, 450m, 600m, 0m, 2);
        var engine = new WashPackage("MOT", "Motor yıkama", 25, 300m, 400m, 0m, 3);
        var detail = new WashPackage("DET", "Detaylı temizlik", 120, 1200m, 1600m, 0m, 4);
        var ceramic = new WashPackage("SER", "Seramik kaplama", 240, 4500m, 6000m, 0m, 5);
        db.WashPackages.AddRange(outside, full, engine, detail, ceramic);

        // ---- Plakalar ve aboneler ----
        var usedPlates = new HashSet<string>();
        string NewPlate()
        {
            while (true)
            {
                var il = rnd.Next(100) switch { < 62 => 34, < 70 => 41, < 76 => 06, < 81 => 16, < 85 => 35, < 89 => 54, < 93 => 59, < 97 => 81, _ => 77 };
                var letters = rnd.Next(100) switch { < 15 => 1, < 60 => 2, _ => 3 };
                var digits = letters switch { 1 => 4, 2 => rnd.Next(2) == 0 ? 3 : 4, _ => rnd.Next(3) == 0 ? 2 : 3 };
                var harf = string.Concat(Enumerable.Range(0, letters).Select(_ => PlateLetters[rnd.Next(PlateLetters.Length)]));
                var no = rnd.Next((int)Math.Pow(10, digits - 1), (int)Math.Pow(10, digits)).ToString();
                var plate = $"{il:00} {harf} {no}";
                if (usedPlates.Add(plate)) return plate;
            }
        }

        var subscriptions = new List<Subscription>();
        for (var i = 0; i < 28; i++)
        {
            var vehicle = i < 4 ? VehicleClass.Large : i < 6 ? VehicleClass.Motorcycle : VehicleClass.Car;
            var name = $"{FirstNames[rnd.Next(FirstNames.Length)]} {LastNames[rnd.Next(LastNames.Length)]}";
            // Başlangıçlar dağınık: bazıları bitmiş, bazıları bu hafta bitiyor
            var start = today.AddDays(-rnd.Next(5, 120));
            var months = i % 9 == 0 ? 1 : rnd.Next(1, 7);
            subscriptions.Add(new Subscription(NewPlate(), name, vehicle, start, months, Subscription.MonthlyFeeFor(vehicle)));
        }
        // İkisi bu hafta bitsin (listede "Yakında bitiyor" görünsün)
        subscriptions[7] = new Subscription(subscriptions[7].Plate, subscriptions[7].OwnerName, VehicleClass.Car, today.AddMonths(-1).AddDays(4), 1, 3000m);
        subscriptions[8] = new Subscription(subscriptions[8].Plate, subscriptions[8].OwnerName, VehicleClass.Car, today.AddMonths(-1).AddDays(2), 1, 3000m);
        db.Subscriptions.AddRange(subscriptions);

        // Düzenli müşteri havuzu: bazı plakalar sık gelir
        var pool = Enumerable.Range(0, 520).Select(_ => (Plate: NewPlate(), Vehicle: rnd.Next(100) switch
        {
            < 80 => VehicleClass.Car,
            < 95 => VehicleClass.Large,
            _ => VehicleClass.Motorcycle,
        })).ToList();

        // ---- Varışlar ----
        var arrivals = new List<(DateTime Entry, TimeSpan Stay, string Plate, VehicleClass Vehicle, Subscription? Sub)>();
        var firstDay = today.AddDays(-HistoryDays);
        for (var d = firstDay; d <= today; d = d.AddDays(1))
        {
            var dow = d.DayOfWeek;
            var weekend = dow is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var baseCount = dow switch { DayOfWeek.Saturday => 85, DayOfWeek.Sunday => 52, DayOfWeek.Friday => 105, _ => 95 };
            var count = (int)(baseCount * (0.88 + rnd.NextDouble() * 0.24));
            for (var k = 0; k < count; k++)
            {
                var (hour, stay) = SampleVisit(rnd, weekend);
                var entry = d.ToDateTime(TimeOnly.MinValue).AddHours(hour);
                // Havuzun başındaki plakalar daha sık seçilir (düzenli müşteriler). Büyük araç yerleri yalnızca büyük araçlara verilir.
                var index = (int)(pool.Count * Math.Pow(rnd.NextDouble(), 1.8));
                var (plate, vehicle) = pool[index];
                arrivals.Add((entry, stay, plate, vehicle, null));
            }
            // Aboneler hafta içi işe gelir
            if (!weekend)
                foreach (var sub in subscriptions.Where(s => s.IsActiveOn(d)))
                {
                    if (rnd.NextDouble() > 0.5) continue;
                    var entry = d.ToDateTime(new TimeOnly(8, 30)).AddMinutes(Normal(rnd, 0, 35));
                    var stay = TimeSpan.FromHours(9.3 + Normal(rnd, 0, 0.8));
                    arrivals.Add((entry, stay, sub.Plate, sub.Vehicle, sub));
                }
        }

        // ---- Simülasyon: yer ataması, çıkışlar, yıkamalar ----
        var freeAt = spots.ToDictionary(s => s, _ => DateTime.MinValue);
        var plateBusyUntil = new Dictionary<string, DateTime>();
        var sessions = new List<ParkingSession>();
        var washes = new List<WashOrder>();

        foreach (var a in arrivals.OrderBy(a => a.Entry))
        {
            if (a.Entry > now) continue;
            if (plateBusyUntil.TryGetValue(a.Plate, out var busy) && busy > a.Entry) continue;

            var spot = spots
                .Where(s => s.Accepts(a.Vehicle) && (a.Vehicle != VehicleClass.Car || s.Kind != SpotKind.Large) && freeAt[s] <= a.Entry && !(s == serviceSpot && a.Entry + a.Stay >= now.Date))
                .OrderBy(s => s.Preference(a.Vehicle)).ThenBy(_ => rnd.Next())
                .FirstOrDefault();
            if (spot is null) continue; // otopark dolu, araç geri döndü

            var exit = a.Entry + a.Stay;
            var session = new ParkingSession(a.Plate, a.Vehicle, spot, a.Entry, a.Sub);
            sessions.Add(session);
            freeAt[spot] = exit;
            plateBusyUntil[a.Plate] = exit;

            // Yıkama: aboneler hariç, en az 50 dakika kalan araçların bir kısmı
            WashOrder? wash = null;
            if (a.Sub is null && a.Vehicle != VehicleClass.Motorcycle && a.Stay.TotalMinutes >= 50 && rnd.NextDouble() < 0.2)
            {
                var package = PickPackage(rnd, a.Stay, outside, full, engine, detail, ceramic);
                var ordered = a.Entry.AddMinutes(rnd.Next(3, 15));
                wash = new WashOrder(a.Plate, a.Vehicle, package, ordered, session);
                var start = ordered.AddMinutes(rnd.Next(5, 35));
                var ready = start.AddMinutes(package.Minutes * (0.85 + rnd.NextDouble() * 0.35));
                if (ready > exit) ready = exit.AddMinutes(-5) < start ? start.AddMinutes(5) : exit.AddMinutes(-5);
                var washer = Washers[rnd.Next(Washers.Length)];
                var status = exit <= now ? WashStatus.Delivered : now < start ? WashStatus.Waiting : now < ready ? WashStatus.Washing : WashStatus.Ready;
                wash.ImportHistory(washer, start, ready, exit <= now ? exit : null, status);
                washes.Add(wash);
            }

            if (exit <= now)
            {
                var washed = wash is not null;
                var fee = TariffEngine.Calculate(Tariff.For(a.Vehicle), a.Entry, exit, washed, a.Sub is not null);
                var payment = a.Sub is not null ? PaymentMethod.Subscription : rnd.NextDouble() < 0.66 ? PaymentMethod.Card : PaymentMethod.Cash;
                session.ImportClosed(exit, fee.Total, wash?.Price ?? 0m, payment);
            }
        }

        // Ayakta yıkama müşterileri (otoparka girmeden): hafta sonu daha çok
        for (var d = firstDay; d <= today; d = d.AddDays(1))
        {
            var weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var count = weekend ? rnd.Next(22, 34) : rnd.Next(9, 15);
            for (var k = 0; k < count; k++)
            {
                var ordered = d.ToDateTime(TimeOnly.MinValue).AddHours(8 + rnd.NextDouble() * 11);
                if (ordered > now) continue;
                var (plate, vehicle) = pool[rnd.Next(pool.Count)];
                if (vehicle == VehicleClass.Motorcycle) vehicle = VehicleClass.Car;
                var package = PickPackage(rnd, TimeSpan.FromHours(1.5), outside, full, engine, detail, ceramic);
                var wash = new WashOrder(plate, vehicle, package, ordered);
                var start = ordered.AddMinutes(rnd.Next(2, 30));
                var ready = start.AddMinutes(package.Minutes * (0.85 + rnd.NextDouble() * 0.3));
                var delivered = ready.AddMinutes(rnd.Next(3, 25));
                var status = delivered <= now ? WashStatus.Delivered : now < start ? WashStatus.Waiting : now < ready ? WashStatus.Washing : WashStatus.Ready;
                if (status == WashStatus.Waiting && rnd.NextDouble() < 0.1) { wash.Cancel(); washes.Add(wash); continue; }
                wash.ImportHistory(Washers[rnd.Next(Washers.Length)], start, ready, delivered, status);
                washes.Add(wash);
            }
        }

        // Kuyruk boş kalmasın: bekleyen bir iki ayakta müşteri
        if (!washes.Any(w => w.Status == WashStatus.Waiting))
        {
            var (plate, _) = pool[rnd.Next(pool.Count)];
            washes.Add(new WashOrder(plate, VehicleClass.Car, full, now.AddMinutes(-6)));
        }

        serviceSpot.TakeOutOfService("Zemin boyası");
        db.Sessions.AddRange(sessions);
        db.WashOrders.AddRange(washes);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Gün içi varış saati ve kalış süresi: sabah işe gelenler uzun, öğle ve akşam gelenler kısa kalır.</summary>
    private static (double Hour, TimeSpan Stay) SampleVisit(Random rnd, bool weekend)
    {
        var p = rnd.NextDouble();
        double hour, stayHours;
        if (!weekend && p < 0.22)
        {
            hour = 8.6 + Normal(rnd, 0, 0.7);
            stayHours = 6.5 + rnd.NextDouble() * 3.5;
        }
        else if (p < (weekend ? 0.45 : 0.66))
        {
            hour = 13 + Normal(rnd, 0, 1.4);
            stayHours = 0.4 + Math.Pow(rnd.NextDouble(), 1.6) * 3.2;
        }
        else if (p < 0.92)
        {
            hour = 18.3 + Normal(rnd, 0, 1.3);
            stayHours = 0.6 + rnd.NextDouble() * 3.4;
        }
        else
        {
            hour = 7 + rnd.NextDouble() * 15;
            stayHours = rnd.NextDouble() < 0.4 ? 12 + rnd.NextDouble() * 30 : 0.15 + rnd.NextDouble() * 1.5; // gece kalan ya da kısa uğrayan
        }
        hour = Math.Clamp(hour, 6.5, 23.4);
        return (hour, TimeSpan.FromMinutes(Math.Round(stayHours * 60)));
    }

    private static WashPackage PickPackage(Random rnd, TimeSpan stay, WashPackage outside, WashPackage full, WashPackage engine, WashPackage detail, WashPackage ceramic)
    {
        var p = rnd.Next(100);
        return p switch
        {
            < 44 => outside,
            < 76 => full,
            < 84 => engine,
            < 96 => stay.TotalHours >= 2.5 ? detail : full,
            _ => stay.TotalHours >= 5 ? ceramic : stay.TotalHours >= 2.5 ? detail : outside,
        };
    }

    private static double Normal(Random rnd, double mean, double sd)
    {
        var u1 = 1.0 - rnd.NextDouble();
        var u2 = rnd.NextDouble();
        return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
