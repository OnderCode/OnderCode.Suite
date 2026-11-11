using Microsoft.EntityFrameworkCore;
using OnderCode.Data.Initialization;
using Otel.Application;
using Otel.Domain;

namespace Otel.Infrastructure;

/// <summary>
/// Demo verisi: 4 katlı, 26 odalı kurgusal "Lale Otel". Geçmiş 75 gün ve gelecek 45 gün için
/// rezervasyonlar bugünün tarihine göre üretilir; uygulama hangi gün açılırsa açılsın gösterge paneli canlı görünür.
/// Bütün kişiler uydurmadır; kimlik ve telefon numarası yoktur.
/// </summary>
public sealed class OtelDemoSeeder(TimeProvider time) : ISeeder<OtelDbContext>
{
    private static readonly string[] FirstNames =
    [
        "Ayşe", "Mehmet", "Zeynep", "Ahmet", "Elif", "Mustafa", "Fatma", "Emre", "Selin", "Burak", "Deniz", "Can",
        "Merve", "Kerem", "Ece", "Oğuz", "Derya", "Serkan", "İrem", "Tolga", "Gizem", "Barış", "Şule", "Onur",
        "Ceren", "Hakan", "Nazlı", "Volkan", "Aslı", "Umut", "Pınar", "Cem", "Hande", "Kaan", "Buse", "Eren",
        "Anna", "Lukas", "Sophie", "James", "Marta", "Ivan",
    ];

    private static readonly string[] LastNames =
    [
        "Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Yıldız", "Öztürk", "Aydın", "Arslan", "Doğan", "Kılıç", "Aslan",
        "Çetin", "Koç", "Kurt", "Özdemir", "Polat", "Erdoğan", "Güneş", "Bulut", "Aksoy", "Ünal", "Tekin", "Karaca",
    ];

    private static readonly string[] ForeignLastNames = ["Müller", "Schmidt", "Rossi", "Smith", "Kowalska", "Petrov"];

    public async Task SeedAsync(OtelDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Rooms.AnyAsync(cancellationToken)) return;

        var today = time.Today();
        var rnd = new Random(2026);

        // Oda tipleri ve odalar
        var standard = new RoomType("STD", "Standart Oda", 2, 2400m);
        var deluxe = new RoomType("DLX", "Deluxe Oda", 3, 3300m);
        var family = new RoomType("FAM", "Aile Odası", 4, 4200m);
        var suite = new RoomType("SUI", "Süit", 2, 7500m);
        db.RoomTypes.AddRange(standard, deluxe, family, suite);

        var rooms = new List<Room>();
        for (var floor = 1; floor <= 3; floor++)
            for (var i = 1; i <= 7; i++)
                rooms.Add(new Room($"{floor}{i:00}", floor, i <= 4 ? standard : i <= 6 ? deluxe : family));
        for (var i = 1; i <= 5; i++)
            rooms.Add(new Room($"4{i:00}", 4, i <= 3 ? deluxe : suite));
        db.Rooms.AddRange(rooms);

        // Sezonlar (bu yıl ve gelecek yıl)
        foreach (var year in new[] { today.Year - 1, today.Year, today.Year + 1 })
        {
            db.Seasons.Add(new Season($"Yaz {year}", new DateOnly(year, 6, 1), new DateOnly(year, 9, 15), 1.35m));
            db.Seasons.Add(new Season($"Yılbaşı {year}", new DateOnly(year, 12, 24), new DateOnly(year + 1, 1, 2), 1.50m));
        }
        await db.SaveChangesAsync(cancellationToken);
        var seasons = await db.Seasons.AsNoTracking().ToListAsync(cancellationToken);

        // Misafirler
        var guests = new List<Guest>();
        var usedNames = new HashSet<string>();
        while (guests.Count < 140)
        {
            var foreign = rnd.NextDouble() < 0.12;
            var first = foreign ? FirstNames[36 + rnd.Next(6)] : FirstNames[rnd.Next(36)];
            var last = foreign ? ForeignLastNames[rnd.Next(ForeignLastNames.Length)] : LastNames[rnd.Next(LastNames.Length)];
            var name = $"{first} {last}";
            if (!usedNames.Add(name)) continue;
            guests.Add(new Guest(name, email: rnd.NextDouble() < 0.6 ? $"{Slug(first)}.{Slug(last)}@example.com" : null, nationality: foreign ? "INT" : "TR"));
        }
        db.Guests.AddRange(guests);

        // Rezervasyonlar: her oda için zaman çizgisinde boşluklu konaklamalar
        var start = today.AddDays(-75);
        var horizon = today.AddDays(45);
        var reservations = new List<Reservation>();
        var sequence = 0;
        foreach (var room in rooms)
        {
            var d = start.AddDays(rnd.Next(0, 3));
            while (d < horizon)
            {
                // Geleceğe doğru doluluk azalır (rezervasyonlar henüz gelmedi)
                var ahead = d.DayNumber - today.DayNumber;
                var fillChance = ahead <= 0 ? 0.80 : ahead < 14 ? 0.62 : 0.38;
                // Haftalık talep deseni: perşembe-cumartesi girişler daha sık, pazar-pazartesi daha seyrek
                fillChance += d.DayOfWeek switch
                {
                    DayOfWeek.Thursday => 0.08,
                    DayOfWeek.Friday or DayOfWeek.Saturday => 0.15,
                    DayOfWeek.Sunday => -0.45,
                    DayOfWeek.Monday => -0.35,
                    _ => 0,
                };
                if (rnd.NextDouble() > fillChance) { d = d.AddDays(rnd.Next(1, 3)); continue; }

                var nights = rnd.Next(1, 100) switch { < 30 => 1, < 55 => 2, < 75 => 3, < 88 => 4, < 95 => 5, _ => 7 };
                if (d.DayOfWeek == DayOfWeek.Friday && rnd.NextDouble() < 0.6) nights = 2; // hafta sonu paketi
                var checkOut = d.AddDays(nights);
                var guest = guests[rnd.Next(guests.Count)];
                var bookedOn = d.AddDays(-rnd.Next(1, 75));
                var quote = PricingEngine.Quote(room.RoomType.BaseRate, d, checkOut, bookedOn, seasons, PricingPolicy.Default);
                var adults = Math.Min(room.RoomType.Capacity, rnd.Next(1, 100) < 65 ? 2 : rnd.Next(1, room.RoomType.Capacity + 1));
                var r = new Reservation(ReservationCodes.Format(bookedOn, ++sequence), guest, room, d, checkOut, adults, quote.Total, bookedOn);

                if (checkOut <= today)
                {
                    r.ImportStatus(rnd.NextDouble() < 0.04 ? ReservationStatus.Cancelled : ReservationStatus.CheckedOut);
                    if (r.Status == ReservationStatus.CheckedOut)
                    {
                        AddCharges(r, rnd);
                        guest.CompleteStay(nights, PricingPolicy.Default.LoyaltyPointsPerNight);
                    }
                }
                else if (d <= today)
                {
                    // Bugün girişi olanların bir kısmı henüz gelmedi
                    r.ImportStatus(d == today && rnd.NextDouble() < 0.5 ? ReservationStatus.Confirmed : ReservationStatus.CheckedIn);
                    if (r.Status == ReservationStatus.CheckedIn)
                    {
                        AddCharges(r, rnd, partial: true);
                        room.MarkOccupied();
                    }
                }
                else if (rnd.NextDouble() < 0.05)
                {
                    r.ImportStatus(ReservationStatus.Cancelled);
                }

                reservations.Add(r);
                d = checkOut.AddDays(rnd.Next(0, 3));
            }
        }

        // Bugün çıkış yapmış odalar kirli; birkaç oda bakımda
        foreach (var r in reservations.Where(r => r.CheckOut == today && r.Status == ReservationStatus.CheckedOut))
        {
            if (r.Room.Status == RoomStatus.Occupied) continue;
            r.Room.MarkDirty();
            db.HousekeepingTasks.Add(new HousekeepingTask(r.Room, TaskKind.Cleaning, $"Çıkış: {r.Code}"));
        }
        var maintenance = rooms.FirstOrDefault(r => r.Number == "306" && r.Status == RoomStatus.Available)
            ?? rooms.LastOrDefault(r => r.Status == RoomStatus.Available);
        if (maintenance is not null)
        {
            maintenance.StartMaintenance("Klima arızası");
            db.HousekeepingTasks.Add(new HousekeepingTask(maintenance, TaskKind.Maintenance, "Klima arızası", "Teknik servis"));
            // Bakımdaki odanın gelecek rezervasyonlarını başka güne kaydırmak yerine iptal et
            foreach (var r in reservations.Where(r => r.Room == maintenance && r.CheckOut > today && r.Status == ReservationStatus.Confirmed))
                r.ImportStatus(ReservationStatus.Cancelled);
        }

        db.Reservations.AddRange(reservations);
    }

    private static void AddCharges(Reservation r, Random rnd, bool partial = false)
    {
        var count = rnd.Next(0, partial ? 3 : 5);
        for (var i = 0; i < count; i++)
        {
            var (category, description, min, max) = rnd.Next(6) switch
            {
                0 => (ChargeCategory.Minibar, "Minibar", 120, 650),
                1 => (ChargeCategory.Restaurant, "Akşam yemeği", 650, 2400),
                2 => (ChargeCategory.Restaurant, "Oda servisi", 300, 1100),
                3 => (ChargeCategory.Spa, "Masaj", 1200, 2800),
                4 => (ChargeCategory.Laundry, "Kuru temizleme", 180, 500),
                _ => (ChargeCategory.Transfer, "Havalimanı transferi", 900, 1600),
            };
            var day = r.CheckIn.AddDays(rnd.Next(Math.Max(1, r.Nights)));
            var at = day.ToDateTime(new TimeOnly(rnd.Next(9, 23), rnd.Next(0, 60)), DateTimeKind.Local).ToUniversalTime();
            r.ImportCharge(category, description, rnd.Next(min, max) / 10 * 10, at);
        }
    }

    private static string Slug(string s) => OnderCode.Kernel.Text.TurkishText.Fold(s).Replace(' ', '.');
}
