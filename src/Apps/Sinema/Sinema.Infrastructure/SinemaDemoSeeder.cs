using Microsoft.EntityFrameworkCore;
using OnderCode.Data.Initialization;
using Sinema.Application;
using Sinema.Domain;

namespace Sinema.Infrastructure;

/// <summary>
/// Örnek veri: 6 salon, 12 film (hepsi uydurma, afişleri özgün), son 21 günün ve önümüzdeki 7 günün seansları.
/// Talep gün gün benzetilir: hafta sonu ve akşam seansları dolu, matineler sakin, çarşamba halk günü kalabalık;
/// yeni çıkan filmler daha çok izlenir. Kişiler gruplar halinde yan yana oturur. Geçmiş seansların biletlerinin çoğu
/// kapıdan okutulmuştur. Müşteri adı yalnızca online satışlarda ve uydurmadır; telefon, e-posta, kimlik yoktur.
/// </summary>
public sealed class SinemaDemoSeeder(TimeProvider time) : ISeeder<SinemaDbContext>
{
    private const int HistoryDays = 21;
    private const int FutureDays = 7;

    public int Order => 0;

    private sealed record FilmSeed(string Title, string Genres, int Minutes, AgeRating Rating, int ReleasedDaysAgo, double Popularity, string Accent,
        string Director, string Cast, string Country, string Synopsis, string Poster, bool Dubbed = true, bool Has3D = false, bool Featured = false);

    private static readonly FilmSeed[] FilmSeeds =
    [
        new("Son Işık", "Bilim kurgu, Dram", 138, AgeRating.ThirteenPlus, 9, 1.0, "#F59E0B", "Deniz Akalın", "Elif Varol, Kerem Toprak, Nisa Ural", "Türkiye",
            "Güneşin söndüğü bir gelecekte, yeraltı şehrinin son mühendisi Ada, yüzeye çıkan eski bir asansörün peşine düşer. Işığa giden yolun bedelini ise herkes başka türlü öder.",
            "son-isik", Has3D: true, Featured: true),
        new("Kayıp Şehrin Sesi", "Macera, Aksiyon", 124, AgeRating.SevenPlus, 16, 0.86, "#14B8A6", "Mert Çağlar", "Ozan Kalkan, Derya Sönmez, Tamer Ulu", "Türkiye",
            "Bir arkeolog ile radyo amatörü kız kardeşi, çölün altından yayın yapan gizemli bir istasyonun izini sürer. Sinyal her gece biraz daha yaklaşmaktadır.",
            "kayip-sehir", Has3D: true),
        new("Mavi Gece", "Gerilim, Suç", 112, AgeRating.SixteenPlus, 4, 0.92, "#3B82F6", "Selin Ergün", "Can Yalın, Melis Arda, Burak Sezer", "Türkiye",
            "İstanbul'da tek bir gece. Taksi şoförü Cem, arka koltukta unutulan bir çantanın yalnızca para değil, bir şehrin sırrını taşıdığını fark eder.",
            "mavi-gece", Dubbed: false),
        new("Pamuk ile Fındık", "Animasyon, Aile, Komedi", 92, AgeRating.General, 23, 0.78, "#EC4899", "Ayça Demirkol", "Seslendirme: İpek Tunç, Murat Aydemir", "Türkiye",
            "Yaşlı bir fırının çatısında yaşayan iki sincap, mahallenin en büyük kek yarışmasını kurtarmak için şehrin öbür ucuna uzanan çılgın bir yolculuğa çıkar.",
            "pamuk-findik", Has3D: true),
        new("Kuzey Rüzgârı", "Dram", 131, AgeRating.ThirteenPlus, 30, 0.48, "#64748B", "Hakan Özdil", "Zeynep Kaya, Ali Rıza Tan", "Türkiye",
            "Karadeniz'de bir balıkçı kasabası. Kırk yıl sonra köyüne dönen Hasan, yarım kalan bir mektubun izini sürerken kendi geçmişiyle yüzleşir.",
            "kuzey-ruzgari"),
        new("Sıfır Noktası", "Aksiyon, Gerilim", 118, AgeRating.ThirteenPlus, 2, 0.95, "#EF4444", "Volkan Er", "Emre Aksu, Gizem Tekin, Serkan Bal", "Türkiye",
            "Bir köprünün ortasında durmuş bir tren, içinde iki yüz yolcu ve geri sayan bir saat. Emekli kurtarma uzmanı Yusuf'un yalnızca bir saati var.",
            "sifir-noktasi"),
        new("Gölgeler Evi", "Korku", 104, AgeRating.EighteenPlus, 12, 0.6, "#8B5CF6", "Ece Tuncer", "Doruk Ay, Pelin Er", "Türkiye",
            "Miras kalan eski bir köşke taşınan genç çift, duvarlardaki gölgelerin her gece bir öncekinden farklı yerde durduğunu fark eder.",
            "golgeler-evi", Dubbed: false),
        new("Bir Yaz Masalı", "Romantik, Komedi", 106, AgeRating.SevenPlus, 19, 0.55, "#F97316", "Gökçe Ilgaz", "Defne Öz, Berk Sarı, Nur Ateş", "Türkiye",
            "Ege'de bir zeytin bahçesini satmaya gelen İstanbullu avukat, bahçenin inatçı bekçisiyle tek bir yaz boyunca süren bir pazarlığa tutuşur.",
            "bir-yaz-masali"),
        new("Demir Kanatlar", "Bilim kurgu, Aksiyon", 142, AgeRating.ThirteenPlus, 37, 0.42, "#06B6D4", "Tolga Erim", "Kaan Uslu, Selin Ak", "Türkiye",
            "Uzay istasyonundaki son pilot, yörüngeden düşmek üzere olan bir şehri kurtarmak için yasaklanmış prototip gemiyle göreve çıkar.",
            "demir-kanatlar", Has3D: true),
        new("Dedemin Bahçesi", "Komedi, Aile", 98, AgeRating.General, 26, 0.5, "#22C55E", "Sinan Koç", "Erdal Yaman, Ela Kır, Umut Bey", "Türkiye",
            "Şehirdeki torunlar, yaz tatilinde dedelerinin köydeki bahçesine sürgün edilir. Ama dedenin bahçesinde domatesten fazlası yetişmektedir.",
            "dedemin-bahcesi"),
        new("Yıldız Tozu", "Fantastik, Macera, Aile", 116, AgeRating.SevenPlus, -9, 0.0, "#A855F7", "Nehir Kaplan", "Lara Güneş, Tuna Erdem", "Türkiye",
            "Gökten düşen bir yıldız parçasını bulan küçük Mira, onu sahibine geri götürmek için masallardaki haritanın gerçek olduğuna inanmak zorundadır.",
            "yildiz-tozu"),
        new("Derin Mavi", "Belgesel", 88, AgeRating.General, -16, 0.0, "#0EA5E9", "Kerem Atlı", "Anlatıcı: Selim Aras", "Türkiye",
            "Türkiye kıyılarının en derin noktalarına inen bir dalış ekibinin iki yıllık yolculuğu. Kameraya ilk kez giren canlılar ve kaybolmak üzere olan resifler.",
            "derin-mavi"),
    ];

    public async Task SeedAsync(SinemaDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Films.AnyAsync(cancellationToken)) return;

        var now = time.Now();
        var today = DateOnly.FromDateTime(now);
        var rnd = new Random(2026);

        // ---- Ayarlar ve fiyatlar ----
        var settings = new CinemaSettings();
        settings.Update("OnderCode Sinema", "Moda", 15, 15, 5, 45, 15, new TimeOnly(10, 30), new TimeOnly(23, 15));
        db.Settings.Add(settings);
        var rules = new PricingRules();
        db.PricingRules.Add(rules);
        var tam = new TicketType("Tam", 260m, "Yetişkin", false, 1);
        var ogrenci = new TicketType("Öğrenci", 190m, "Öğrenci belgesi gösterilir", true, 2);
        var cocuk = new TicketType("Çocuk", 170m, "12 yaş ve altı", false, 3);
        var yasli = new TicketType("65+", 170m, "65 yaş ve üzeri, kimlik gösterilir", true, 4);
        var types = new[] { tam, ogrenci, cocuk, yasli };
        db.TicketTypes.AddRange(types);

        // ---- Büfe ----
        var items = new List<ConcessionItem>
        {
            new("Mısır (küçük)", ConcessionCategory.Popcorn, 95m, 1), new("Mısır (orta)", ConcessionCategory.Popcorn, 125m, 2), new("Mısır (büyük)", ConcessionCategory.Popcorn, 150m, 3),
            new("Kola", ConcessionCategory.Drink, 75m, 4), new("Ayran", ConcessionCategory.Drink, 45m, 5), new("Su", ConcessionCategory.Drink, 25m, 6), new("Soğuk çay", ConcessionCategory.Drink, 70m, 7),
            new("Nachos", ConcessionCategory.Snack, 120m, 8), new("Çikolata", ConcessionCategory.Snack, 55m, 9), new("Pamuk şeker", ConcessionCategory.Snack, 60m, 10),
            new("Klasik menü", ConcessionCategory.Menu, 185m, 11), new("Çift menü", ConcessionCategory.Menu, 340m, 12), new("Çocuk menü", ConcessionCategory.Menu, 150m, 13),
        };
        db.ConcessionItems.AddRange(items);

        // ---- Personel (yalnızca ad, görev, işe giriş) ----
        var staff = new List<Employee>
        {
            new("Gülay Ertem", StaffRole.Manager, new DateOnly(2018, 4, 2)),
            new("Barış Kılınç", StaffRole.Manager, new DateOnly(2021, 9, 13)),
            new("Ceyda Arıkan", StaffRole.BoxOffice, new DateOnly(2022, 6, 1)),
            new("Onur Yazıcı", StaffRole.BoxOffice, new DateOnly(2023, 11, 20)),
            new("Sena Tokgöz", StaffRole.BoxOffice, new DateOnly(2025, 2, 3)),
            new("Kadir Öner", StaffRole.Usher, new DateOnly(2020, 1, 15)),
            new("Merve Doğru", StaffRole.Usher, new DateOnly(2024, 7, 8)),
            new("Emir Sezgin", StaffRole.Usher, new DateOnly(2026, 3, 1)),
        };
        staff[0].SetShift("Hafta içi gündüz");
        staff[1].SetShift("Akşam ve hafta sonu");
        staff[2].SetShift("Gündüz gişe");
        staff[3].SetShift("Akşam gişe");
        staff[4].SetShift("Hafta sonu");
        staff[5].SetShift("Gündüz kapı");
        staff[6].SetShift("Akşam kapı");
        staff[7].SetShift("Hafta sonu kapı");
        db.Employees.AddRange(staff);
        var cashiers = staff.Where(s => s.Role == StaffRole.BoxOffice).ToList();
        var ushers = staff.Where(s => s.Role == StaffRole.Usher).ToList();

        // ---- Salonlar ----
        var halls = new List<Hall>
        {
            new("Salon 1", HallLayout.Build(13, 20, [4, 16], vipRows: 2, accessibleRow: 12), "Dolby Atmos · Lazer projeksiyon", true),
            new("Salon 2", HallLayout.Build(11, 16, [3, 13], vipRows: 0, accessibleRow: 10), "Dolby 7.1", true),
            new("Salon 3", HallLayout.Build(10, 14, [3, 11], vipRows: 0, accessibleRow: 9), "Dolby 7.1"),
            new("Salon 4", HallLayout.Build(9, 12, [6], vipRows: 0, accessibleRow: 8), "Dolby 5.1"),
            new("Salon 5", HallLayout.Build(7, 10, [5], vipRows: 0, accessibleRow: 6), "Dolby 5.1"),
            new("VIP Salon", "VV.VVVV.VV/VV.VVVV.VV/VV.VVVV.VV/........../CC.CCCC.CC/CC.CCCC.CC", "Yatar koltuk · Servis", false),
        };
        for (var i = 0; i < halls.Count; i++) halls[i].SetSortOrder(i + 1);
        db.Halls.AddRange(halls);

        // ---- Filmler ----
        var films = new List<(Film Film, FilmSeed Seed)>();
        foreach (var f in FilmSeeds)
        {
            var film = new Film(f.Title, f.Genres, f.Minutes, f.Rating, today.AddDays(-f.ReleasedDaysAgo));
            film.SetCredits(f.Director, f.Cast, f.Country, f.Synopsis);
            film.SetPoster($"posters/{f.Poster}.jpg", f.Accent);
            film.SetStatus(f.ReleasedDaysAgo < 0 ? FilmStatus.ComingSoon : FilmStatus.NowShowing);
            film.SetFeatured(f.Featured);
            films.Add((film, f));
        }
        db.Films.AddRange(films.Select(f => f.Film));
        await db.SaveChangesAsync(cancellationToken);

        // ---- Seanslar ----
        var sessions = new List<(Session Session, FilmSeed Seed)>();
        var layouts = halls.ToDictionary(h => h.Id, h => h.ParsedLayout());
        for (var d = -HistoryDays; d <= FutureDays; d++)
        {
            var day = today.AddDays(d);
            var showing = films.Where(f => day >= f.Film.ReleaseDate && f.Seed.Popularity > 0).OrderByDescending(f => Demand(f.Seed, day, today)).ToList();
            if (showing.Count == 0) continue;
            for (var h = 0; h < halls.Count; h++)
            {
                var hall = halls[h];
                // Büyük salonlar en çok izlenen filmleri, küçükler kalanları alır; VIP salon karışık
                var pool = h switch
                {
                    0 => showing.Take(2).ToList(),
                    1 => showing.Skip(1).Take(3).ToList(),
                    2 => showing.Skip(2).Take(3).ToList(),
                    3 => showing.Skip(4).Take(3).ToList(),
                    4 => showing.Skip(6).Take(4).ToList(),
                    _ => showing.Take(4).ToList(),
                };
                if (pool.Count == 0) pool = showing.Take(2).ToList();
                var start = day.ToDateTime(new TimeOnly(10, 30)).AddMinutes(h * 15 + rnd.Next(0, 3) * 5);
                var k = (d + h + 100) % pool.Count;
                while (TimeOnly.FromDateTime(start) <= settings.LastSessionAt && start.Date == day.ToDateTime(TimeOnly.MinValue))
                {
                    var (film, seed) = pool[k++ % pool.Count];
                    var threeD = hall.Supports3D && seed.Has3D && rnd.NextDouble() < 0.4;
                    var lang = seed.Dubbed ? (seed.Rating >= AgeRating.ThirteenPlus && rnd.NextDouble() < 0.35 ? SessionLanguage.Subtitled : SessionLanguage.Dubbed) : SessionLanguage.Subtitled;
                    var session = new Session(film, hall, start, threeD ? SessionFormat.ThreeD : SessionFormat.TwoD, lang, settings);
                    sessions.Add((session, seed));
                    var next = session.ReadyAt.AddMinutes(rnd.Next(0, 3) * 5);
                    start = new DateTime(next.Year, next.Month, next.Day, next.Hour, next.Minute / 5 * 5, 0).AddMinutes(next.Minute % 5 == 0 ? 0 : 5);
                }
            }
        }
        db.Sessions.AddRange(sessions.Select(s => s.Session));
        await db.SaveChangesAsync(cancellationToken);

        // ---- Satışlar ----
        string[] first = ["Ayşe", "Mehmet", "Zeynep", "Ahmet", "Elif", "Mustafa", "Merve", "Emre", "Selin", "Burak", "Deniz", "Can", "Ece", "Kaan", "İrem", "Oğuz", "Buse", "Tolga", "Gizem", "Uğur", "Nil", "Arda", "Pınar", "Serkan", "Damla", "Efe", "Yasemin", "Hakan", "Melis", "Cem"];
        string[] last = ["Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Yıldız", "Aydın", "Öztürk", "Arslan", "Doğan", "Kılıç", "Aslan", "Çetin", "Kara", "Koç", "Kurt", "Özdemir", "Polat", "Erdem", "Tekin", "Güneş", "Acar", "Bulut", "Ateş"];
        var codes = new HashSet<string>();
        var orders = new List<Order>();
        foreach (var (session, seed) in sessions)
        {
            var layout = layouts[session.HallId];
            var dayOffset = DateOnly.FromDateTime(session.Start).DayNumber - today.DayNumber;
            var expected = Demand(seed, DateOnly.FromDateTime(session.Start), today) * TimeFactor(session.Start) * (0.8 + rnd.NextDouble() * 0.4);
            if (session.HallId == halls[^1].Id) expected *= 0.85;
            double fill = Math.Min(0.97, expected);
            if (session.Start > now)
            {
                // ileri tarihli seanslarda yalnızca ön satış
                var hoursAhead = (session.Start - now).TotalHours;
                fill *= hoursAhead < 3 ? 0.75 : hoursAhead < 12 ? 0.5 : dayOffset <= 1 ? 0.32 : dayOffset <= 3 ? 0.16 : 0.07;
            }
            var target = (int)Math.Round(layout.Capacity * fill);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guard = 0;
            while (taken.Count < target && guard++ < 400)
            {
                var size = Pick(rnd, [1, 2, 3, 4, 5], [0.18, 0.46, 0.17, 0.14, 0.05]);
                if (session.Hall!.Name == "VIP Salon") size = Math.Min(size, 2);
                var seats = FindSeats(layout, taken, size, rnd);
                if (seats.Count == 0) break;
                var future = session.Start > now;
                var online = rnd.NextDouble() < (future ? 0.8 : session.Start.Hour >= 18 ? 0.55 : 0.35);
                DateTime soldAt;
                if (future) soldAt = now.AddMinutes(-rnd.Next(5, 60 * 48));
                else if (online) soldAt = session.Start.AddHours(-Math.Max(0.4, rnd.NextDouble() * rnd.NextDouble() * 96));
                else soldAt = session.Start.AddMinutes(-rnd.Next(5, 150));
                if (soldAt > now) soldAt = now.AddMinutes(-rnd.Next(1, 30));
                var channel = online ? SalesChannel.Online : SalesChannel.BoxOffice;
                var payment = online ? PaymentMethod.Online : rnd.NextDouble() < 0.72 ? PaymentMethod.Card : PaymentMethod.Cash;
                string code;
                do code = TicketCode.New(); while (!codes.Add(code));
                var customer = online ? $"{first[rnd.Next(first.Length)]} {last[rnd.Next(last.Length)]}" : null;
                var order = new Order(code, session, channel, payment, soldAt, customer, online ? null : cashiers[rnd.Next(cashiers.Count)].Id);
                var kids = seed.Genres.Contains("Animasyon") || seed.Genres.Contains("Aile");
                for (var i = 0; i < seats.Count; i++)
                {
                    var seat = seats[i];
                    var type = kids && i > 0 && rnd.NextDouble() < 0.55 ? cocuk
                        : Pick(rnd, types, kids ? [0.6, 0.15, 0.15, 0.1] : [0.58, 0.31, 0.03, 0.08]);
                    if (seed.Rating >= AgeRating.ThirteenPlus && type == cocuk) type = ogrenci;
                    order.AddTicket(seat.Code, seat.Type, type, PricingEngine.Quote(type, seat.Type, session.Format, session.Start, rules));
                    taken.Add(seat.Code);
                }
                if (rnd.NextDouble() < (online ? 0.18 : 0.42))
                {
                    var menu = kids && rnd.NextDouble() < 0.4 ? items[12] : seats.Count >= 2 && rnd.NextDouble() < 0.35 ? items[11] : items[rnd.Next(0, 11)];
                    order.AddConcession(menu, 1);
                    if (rnd.NextDouble() < 0.5) order.AddConcession(items[rnd.Next(3, 7)], rnd.Next(1, 3));
                }
                // geçmiş seanslarda biletlerin çoğu kapıdan okutulmuş
                if (session.Start.AddMinutes(10) < now)
                {
                    foreach (var t in order.Tickets)
                        if (rnd.NextDouble() < 0.94) t.CheckIn(session.Start.AddMinutes(-rnd.Next(-8, 25)), ushers[rnd.Next(ushers.Count)].Id);
                }
                else if (rnd.NextDouble() < 0.015 && order.Tickets.All(t => t.Status == TicketStatus.Valid))
                {
                    order.Refund(soldAt.AddHours(rnd.NextDouble() * Math.Max(0.1, (now - soldAt).TotalHours)));
                    foreach (var s in seats) taken.Remove(s.Code);
                }
                orders.Add(order);
            }
        }

        // Gişeden yalnız büfe satışı
        for (var d = -HistoryDays; d <= 0; d++)
        {
            var day = today.AddDays(d);
            for (var i = 0; i < 6 + rnd.Next(10); i++)
            {
                var at = day.ToDateTime(new TimeOnly(11, 0)).AddMinutes(rnd.Next(0, 660));
                if (at > now) continue;
                string code;
                do code = TicketCode.New(); while (!codes.Add(code));
                var o = new Order(code, null, SalesChannel.BoxOffice, rnd.NextDouble() < 0.7 ? PaymentMethod.Card : PaymentMethod.Cash, at, null, cashiers[rnd.Next(cashiers.Count)].Id);
                o.AddConcession(items[rnd.Next(items.Count)], 1);
                if (rnd.NextDouble() < 0.5) o.AddConcession(items[rnd.Next(3, 7)], 1);
                orders.Add(o);
            }
        }
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Filmin o günkü talebi: popülerlik × gün × tazelik.</summary>
    static double Demand(FilmSeed f, DateOnly day, DateOnly today)
    {
        var week = day.DayOfWeek switch
        {
            DayOfWeek.Monday => 0.5, DayOfWeek.Tuesday => 0.55, DayOfWeek.Wednesday => 0.85, DayOfWeek.Thursday => 0.65,
            DayOfWeek.Friday => 0.95, DayOfWeek.Saturday => 1.2, _ => 1.1,
        };
        var age = Math.Max(0, day.DayNumber - (today.DayNumber - f.ReleasedDaysAgo));
        var fresh = Math.Max(0.45, 1.15 - age * 0.02);
        return f.Popularity * week * fresh * 0.62;
    }

    static double TimeFactor(DateTime start) => start.Hour switch
    {
        < 13 => 0.45,
        < 16 => 0.7,
        < 19 => 0.95,
        < 22 => 1.25,
        _ => 0.75,
    };

    static T Pick<T>(Random rnd, T[] values, double[] weights)
    {
        var x = rnd.NextDouble() * weights.Sum();
        for (var i = 0; i < values.Length; i++)
        {
            x -= weights[i];
            if (x <= 0) return values[i];
        }
        return values[^1];
    }

    /// <summary>Grubu yan yana oturtur; tercih orta-arka sıralar. Yer yoksa boş koltuk döner.</summary>
    static List<SeatInfo> FindSeats(HallLayout layout, HashSet<string> taken, int size, Random rnd)
    {
        var rows = layout.Seats.GroupBy(s => s.Row).OrderBy(g => g.Key).ToList();
        if (rows.Count == 0) return [];
        for (var attempt = 0; attempt < 12; attempt++)
        {
            // orta-arka sıralara ağırlık
            var t = Math.Clamp(0.62 + (rnd.NextDouble() - 0.5) * 0.9 * (1 + attempt * 0.1), 0, 0.999);
            var row = rows[(int)(t * rows.Count)].OrderBy(s => s.Col).ToList();
            var runs = new List<List<SeatInfo>>();
            var cur = new List<SeatInfo>();
            SeatInfo? prev = null;
            foreach (var s in row)
            {
                if (taken.Contains(s.Code) || (prev is not null && s.Col != prev.Col + 1)) { if (cur.Count > 0) runs.Add(cur); cur = []; }
                if (!taken.Contains(s.Code)) cur.Add(s);
                prev = s;
            }
            if (cur.Count > 0) runs.Add(cur);
            var fit = runs.Where(r => r.Count >= size).ToList();
            if (fit.Count == 0) continue;
            var run = fit[rnd.Next(fit.Count)];
            // ortaya yakın başla
            var center = layout.Cols / 2.0;
            var bestStart = Enumerable.Range(0, run.Count - size + 1).OrderBy(i => Math.Abs(run[i].Col + size / 2.0 - center) + rnd.NextDouble() * 3).First();
            return run.Skip(bestStart).Take(size).ToList();
        }
        var free = layout.Seats.Where(s => !taken.Contains(s.Code)).ToList();
        return free.Count == 0 ? [] : free.OrderBy(_ => rnd.Next()).Take(Math.Min(size, free.Count)).ToList();
    }
}
