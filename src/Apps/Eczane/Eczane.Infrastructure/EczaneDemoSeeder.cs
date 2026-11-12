using Eczane.Application;
using Eczane.Domain;
using Microsoft.EntityFrameworkCore;
using OnderCode.Data.Initialization;

namespace Eczane.Infrastructure;

/// <summary>
/// Örnek veri: 7 personel, ~190 ürün, 4 ecza deposu, 10 kurum, 1.100 hasta ve son 63 günün eczane işleyişi.
/// Gün gün benzetim yapılır: sabah depodan gelen siparişler partilere girer, gün boyu reçete ve serbest satış yapılır
/// (FEFO), akşam stoğu azalanlar için sipariş açılır, cuma günü vadesi gelen depo faturaları ödenir, ay başında SKT'si
/// geçen partiler imha edilir. Kronik hastalar ilaçlarını yaklaşık 28 günde bir alır. Pazar yalnızca nöbet günü açıktır.
/// Bütün kişi, kurum ve depo adları uydurmadır; hastalarda TC kimlik no, telefon ve adres yoktur.
/// </summary>
public sealed class EczaneDemoSeeder(TimeProvider time) : ISeeder<EczaneDbContext>
{
    private const int HistoryDays = 63;
    private const string RxChars = "ABCDEFGHJKLMNPRSTUVYZ123456789";

    public int Order => 0;

    private sealed class Chronic(Patient patient, Hospital hospital, string branch, List<Product> regimen, int interval, DateOnly next)
    {
        public Patient Patient { get; } = patient;
        public Hospital Hospital { get; } = hospital;
        public string Branch { get; } = branch;
        public List<Product> Regimen { get; } = regimen;
        public int Interval { get; } = interval;
        public DateOnly Next { get; set; } = next;
    }

    private sealed record Invoice(Wholesaler Wholesaler, PurchaseOrder Order, decimal Amount, DateOnly Due)
    {
        public bool Paid { get; set; }
    }

    public async Task SeedAsync(EczaneDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Products.AnyAsync(cancellationToken)) return;

        var now = time.Now();
        var today = DateOnly.FromDateTime(now);
        var day0 = today.AddDays(-HistoryDays);
        var rnd = new Random(2026);

        // ---- Personel (kimlik/telefon/adres yok) ----
        var owner = new Employee("Selin Aydemir", StaffRole.ResponsiblePharmacist, new DateOnly(2014, 9, 1), 98_000m);
        var ph2 = new Employee("Mert Kalkan", StaffRole.Pharmacist, new DateOnly(2020, 3, 16), 74_000m);
        var ph3 = new Employee("Duygu Erten", StaffRole.Pharmacist, new DateOnly(2023, 10, 2), 69_000m);
        var tech1 = new Employee("Hakan Tunç", StaffRole.Technician, new DateOnly(2017, 5, 8), 44_000m);
        var tech2 = new Employee("Büşra Yalın", StaffRole.Technician, new DateOnly(2021, 11, 1), 40_000m);
        var asst = new Employee("Okan Sezer", StaffRole.Assistant, new DateOnly(2024, 2, 12), 34_000m);
        var intern = new Employee("Ceren Uslu", StaffRole.Intern, new DateOnly(2026, 9, 15), 12_000m);
        var staff = new[] { owner, ph2, ph3, tech1, tech2, asst, intern };
        db.Employees.AddRange(staff);

        var leaves = new List<LeaveRecord>
        {
            new(ph2, LeaveType.Annual, day0.AddDays(12), day0.AddDays(21), "Yaz izninin kalanı"),
            new(tech1, LeaveType.Sick, day0.AddDays(33), day0.AddDays(34)),
            new(tech2, LeaveType.Annual, today.AddDays(-2), today.AddDays(3), "Aile ziyareti"),
            new(asst, LeaveType.Excuse, today.AddDays(-20), today.AddDays(-20), "Taşınma"),
            new(ph3, LeaveType.Annual, today.AddDays(9), today.AddDays(16)),
            new(owner, LeaveType.Annual, day0.AddDays(2), day0.AddDays(6)),
        };
        db.Leaves.AddRange(leaves);
        bool OnLeave(Employee e, DateOnly d) => leaves.Any(l => l.Employee == e && l.Covers(d));

        // ---- Nöbet günleri (yıl başından): her 10 günde bir gece nöbeti + her ayın ilk pazarı + bayramların ilk günü ----
        var duties = new List<DutyShift>();
        var dutyEnd = new DateOnly(today.Year, today.Month, 1).AddMonths(2).AddDays(-1);
        var holidays = new HashSet<DateOnly> { new(today.Year, 3, 20), new(today.Year, 5, 27), new(today.Year, 10, 29), new(today.Year, 1, 1), new(today.Year, 4, 23), new(today.Year, 5, 19), new(today.Year, 8, 30) };
        for (var d = new DateOnly(today.Year, 1, 1); d <= dutyEnd; d = d.AddDays(1))
        {
            var firstSunday = d.DayOfWeek == DayOfWeek.Sunday && d.Day <= 7;
            if (holidays.Contains(d)) duties.Add(new DutyShift(d, DutyKind.Holiday, "Resmî tatil"));
            else if (firstSunday) duties.Add(new DutyShift(d, DutyKind.Sunday));
            else if (d.DayNumber % 10 == 3 && d.DayOfWeek != DayOfWeek.Sunday) duties.Add(new DutyShift(d, DutyKind.Night));
        }
        db.DutyShifts.AddRange(duties);
        // Bu ayın sonuna kadar dağıtılır; gelecek ay "Nöbetleri dağıt" ile yapılsın diye boş kalır
        var monthEnd = new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1);
        var people = staff.Where(e => e.Role != StaffRole.Intern && e.HireDate < new DateOnly(today.Year, 1, 1)).Select((e, i) => (e, p: new DutyPlanner.Person(i + 1, e.FullName, e.IsPharmacist))).ToList();
        var plan = DutyPlanner.Plan(duties.Where(s => s.Date <= monthEnd).Select(s => (s.Date, s.Kind)), people.Select(x => x.p).ToList(),
            (id, d) => OnLeave(people.First(x => x.p.Id == id).e, d));
        foreach (var a in plan)
        {
            if (a.PharmacistId is not int pid) continue;
            var ph = people.First(x => x.p.Id == pid).e;
            var helper = a.AssistantId is int aid ? people.First(x => x.p.Id == aid).e : null;
            duties.First(s => s.Date == a.Date).Assign(ph, helper);
        }
        var dutyByDate = duties.ToDictionary(s => s.Date);

        // ---- Kurumlar, depolar ----
        var hospitals = DemoCatalog.Hospitals.Select(h => (Entity: new Hospital(h.Name, h.Kind), h.Weight, h.Branches)).ToList();
        db.Hospitals.AddRange(hospitals.Select(h => h.Entity));
        var wholesalers = new[]
        {
            new Wholesaler("Kuzey Ecza Deposu", 3, 30, 0.09m),
            new Wholesaler("Pınar Ecza Deposu", 2, 45, 0.10m),
            new Wholesaler("Doğuş Ecza Deposu", 2, 21, 0.08m),
            new Wholesaler("Atlas Ecza Deposu", 1, 60, 0.11m),
        };
        db.Wholesalers.AddRange(wholesalers);

        // ---- Ürünler ----
        var items = DemoCatalog.Items;
        var products = new List<Product>();
        var meta = new Dictionary<Product, DemoCatalog.Item>();
        for (var i = 0; i < items.Length; i++)
        {
            var it = items[i];
            var barcode = Ean13.Complete($"2000{i + 1:00000000}");
            var cost = Math.Round(it.Price * (it.Category switch { ProductCategory.Rx => 0.78m, ProductCategory.Otc => 0.72m, _ => 0.66m }) * 0.91m, 2);
            var min = it.Weight >= 6 ? 6 : it.Weight >= 3 ? 4 : it.Weight >= 1 ? 2 : 1;
            var shelf = it.Cold ? "BUZDOLABI" : $"{(char)('A' + i % 8)}-{1 + i % 12:00}";
            var p = new Product(barcode, it.Name, it.Category, it.Price, cost, it.Ingredient, it.Group, it.Color, it.Cold, it.Reimbursed, min, shelf);
            products.Add(p);
            meta[p] = it;
        }
        db.Products.AddRange(products);
        var rxProducts = products.Where(p => p.Category == ProductCategory.Rx).ToList();
        var retailProducts = products.Where(p => p.Category != ProductCategory.Rx).ToList();
        var byGroup = rxProducts.GroupBy(p => p.Group!).ToDictionary(g => g.Key, g => g.ToList());

        // ---- Mal fazlası kampanyaları ----
        var campaigns = new List<FreeGoodsCampaign>();
        foreach (var p in products.Where(p => meta[p].Weight >= 2).OrderBy(_ => rnd.Next()).Take(46))
        {
            var w = wholesalers[rnd.Next(wholesalers.Length)];
            var (th, fr) = p.Category == ProductCategory.Rx ? (rnd.Next(2) == 0 ? (10, 1) : (20, 3)) : (rnd.Next(2) == 0 ? (5, 1) : (10, 2));
            var until = rnd.NextDouble() < 0.15 ? today.AddDays(-rnd.Next(3, 30)) : today.AddDays(rnd.Next(10, 75));
            campaigns.Add(new FreeGoodsCampaign(w, p, th, fr, until));
        }
        db.Campaigns.AddRange(campaigns);

        // ---- Hastalar (TC/telefon/adres yok) ----
        var patients = new List<Patient>();
        for (var i = 0; i < 1100; i++)
        {
            var female = rnd.NextDouble() < 0.54;
            var first = female ? DemoCatalog.FirstNamesF[rnd.Next(DemoCatalog.FirstNamesF.Length)] : DemoCatalog.FirstNamesM[rnd.Next(DemoCatalog.FirstNamesM.Length)];
            var last = DemoCatalog.LastNames[rnd.Next(DemoCatalog.LastNames.Length)];
            var age = (int)Math.Clamp(Normal(rnd, 48, 20), 1, 92);
            var ins = rnd.NextDouble() switch { < 0.86 => InsuranceType.Sgk, < 0.94 => InsuranceType.Private, _ => InsuranceType.None };
            var retired = age >= 58 && rnd.NextDouble() < 0.85;
            var chronic = ins == InsuranceType.Sgk && age >= 45 && rnd.NextDouble() < 0.35;
            string? note = rnd.NextDouble() < 0.05 ? (rnd.Next(3) switch { 0 => "İlaçlarını ay başında alır", 1 => "Yakını teslim alıyor", _ => "Kutu yerine blister tercih ediyor" }) : null;
            patients.Add(new Patient($"{first} {last}", today.Year - age, female ? Gender.Female : Gender.Male, ins, retired ? SgkStatus.Retired : SgkStatus.Worker, chronic, note));
        }
        db.Patients.AddRange(patients);

        // Kronik hastalar: düzenli ilaç listesi, kendi aile sağlığı merkezi ya da poliklinik, ~28 günde bir
        var chronicGroups = new[] { "Kardiyovasküler", "Kardiyovasküler", "Diyabet", "Tiroid", "Mide-bağırsak", "Solunum", "Psikiyatri", "Nöroloji", "Üroloji" };
        var chronics = new List<Chronic>();
        foreach (var p in patients.Where(p => p.AgeIn(today.Year) >= 40).OrderBy(_ => rnd.Next()).Take(560))
        {
            var count = rnd.Next(1, 5);
            var regimen = new List<Product>();
            for (var k = 0; k < count; k++)
            {
                var group = chronicGroups[rnd.Next(chronicGroups.Length)];
                var pool = byGroup[group].Where(x => meta[x].Chronic && x.Color == PrescriptionColor.White).ToList();
                if (pool.Count == 0) continue;
                var pick = Pick(rnd, pool, x => meta[x].Weight);
                if (!regimen.Contains(pick)) regimen.Add(pick);
            }
            if (regimen.Count == 0) continue;
            var asm = rnd.NextDouble() < 0.72;
            var (h, branch) = asm
                ? (hospitals[rnd.Next(3)].Entity, "Aile hekimliği")
                : (hospitals[rnd.Next(3, 5)].Entity, regimen.Any(x => x.Group == "Diyabet") ? "Endokrinoloji" : regimen.Any(x => x.Group == "Kardiyovasküler") ? "Kardiyoloji" : "İç hastalıkları");
            chronics.Add(new Chronic(p, h, branch, regimen, rnd.Next(26, 32), day0.AddDays(rnd.Next(0, 30))));
        }
        // Biyolojik ilaç kullanan birkaç hasta (üniversite hastanesi, soğuk zincir, yüksek tutar)
        var university = hospitals.First(h => h.Entity.Kind == HospitalKind.University).Entity;
        foreach (var (p, drug, branch) in new[] { (patients[11], "Adalimumab", "Dermatoloji"), (patients[47], "Adalimumab", "Endokrinoloji"), (patients[203], "Teriparatid", "Endokrinoloji") })
            chronics.Add(new Chronic(p, university, branch, [rxProducts.First(x => x.Name.StartsWith(drug))], 28, day0.AddDays(rnd.Next(0, 28))));

        // ---- Açılış stoğu ----
        var lots = products.ToDictionary(p => p, _ => new List<StockLot>());
        var totalWeightRx = rxProducts.Sum(p => meta[p].Weight);
        var totalWeightRetail = retailProducts.Sum(p => meta[p].Weight);
        double Expected(Product p) => p.Category == ProductCategory.Rx ? meta[p].Weight / totalWeightRx * 260 : meta[p].Weight / totalWeightRetail * 110;
        var allLots = new List<StockLot>();
        var movements = new List<StockMovement>();
        foreach (var p in products)
        {
            var qty = (int)Math.Ceiling(Expected(p) * 30) + p.MinStock + rnd.Next(0, 3);
            var months = meta[p].Cold ? rnd.Next(5, 11) : rnd.Next(8, 30);
            var expiry = day0.AddMonths(months).AddDays(rnd.Next(0, 28));
            // Bir kısım ürünün açılış partisi SKT'ye yakın: SKT takibi ekranı gerçekçi dolsun
            if (rnd.NextDouble() < 0.10) expiry = today.AddDays(rnd.Next(-25, 85));
            if (expiry <= day0.AddDays(1)) expiry = day0.AddDays(rnd.Next(5, 40));
            var lot = new StockLot(p, $"A{p.Barcode[^5..]}{rnd.Next(10, 99)}", expiry, qty, p.PurchasePrice, day0.ToDateTime(new TimeOnly(8, 0)));
            lots[p].Add(lot);
            allLots.Add(lot);
            // Yavaş dönen ürünlerde elde kalmış eski parti: benzetim sonunda SKT'si yakın / geçmiş partiler oluşur
            if (Expected(p) < 0.45 && rnd.NextDouble() < 0.4)
            {
                var old = new StockLot(p, $"E{p.Barcode[^5..]}{rnd.Next(10, 99)}", today.AddDays(rnd.Next(-18, 88)),
                    (int)(Expected(p) * 63) + rnd.Next(3, 9), Math.Round(p.PurchasePrice * 0.97m, 2), day0.ToDateTime(new TimeOnly(8, 0)));
                lots[p].Add(old);
                allLots.Add(old);
            }
        }

        // ---- Gün gün benzetim ----
        var sales = new List<Sale>();
        var orders = new List<PurchaseOrder>();
        var invoices = new List<Invoice>();
        var payments = new List<SupplierPayment>();
        var soldByDay = new Dictionary<(Product, DateOnly), int>();
        var orderSeq = 0;

        for (var d = day0; d <= today; d = d.AddDays(1))
        {
            var isToday = d == today;
            var duty = dutyByDate.GetValueOrDefault(d);
            var open = d.DayOfWeek != DayOfWeek.Sunday || duty is not null;

            // Ay başı: SKT'si geçmiş partiler imha
            if (d.Day == 1)
                foreach (var lot in allLots.Where(l => l.Quantity > 0 && l.Expiry < d.AddDays(-20)))
                {
                    var qty = lot.Quantity;
                    lot.AdjustTo(0);
                    movements.Add(new StockMovement(lot, MovementType.Disposal, -qty, d.ToDateTime(new TimeOnly(9, 15)), "SKT geçti, imha tutanağı"));
                }

            // Sabah: dünkü siparişlerin teslimi
            foreach (var o in orders.Where(o => o.Status == OrderStatus.Ordered && o.OrderedAt!.Value.Date < d.ToDateTime(TimeOnly.MinValue)))
            {
                if (!open && !isToday) break;
                var at = d.ToDateTime(new TimeOnly(9, 0)).AddMinutes(rnd.Next(0, 150) / o.Wholesaler.DeliveriesPerDay);
                if (isToday && at > now) continue;
                var received = o.Receive(at, line =>
                {
                    var months = line.Product.ColdChain ? rnd.Next(8, 14) : rnd.Next(14, 34);
                    return ($"{(char)('K' + o.Wholesaler.Name.Length % 6)}{line.Product.Barcode[^4..]}{d:yyMM}{rnd.Next(1, 9)}", d.AddMonths(months).AddDays(rnd.Next(0, 28)));
                });
                foreach (var lot in received)
                {
                    lots[lot.Product].Add(lot);
                    allLots.Add(lot);
                    movements.Add(new StockMovement(lot, MovementType.Receive, lot.Quantity, at, o.Number));
                }
                invoices.Add(new Invoice(o.Wholesaler, o, o.Total, o.DueDate!.Value));
            }

            // Cuma: önümüzdeki 7 gün içinde vadesi gelen faturaları öde (Doğuş'a son iki haftanın ödemesi gecikmiş)
            if (d.DayOfWeek == DayOfWeek.Friday && (!isToday || now.Hour >= 15))
                foreach (var g in invoices.Where(i => !i.Paid && i.Due <= d.AddDays(7)).GroupBy(i => i.Wholesaler))
                {
                    var due = g.ToList();
                    if (g.Key.Name.StartsWith("Doğuş") && d > today.AddDays(-21)) due = due.Where(i => i.Due < d.AddDays(-14)).ToList();
                    if (due.Count == 0) continue;
                    foreach (var i in due) i.Paid = true;
                    var method = due.Sum(i => i.Amount) > 60_000m && rnd.Next(3) == 0 ? SupplierPaymentMethod.Check : SupplierPaymentMethod.Transfer;
                    payments.Add(new SupplierPayment(g.Key, d, due.Sum(i => i.Amount), method, $"{due.Count} fatura"));
                }

            if (open)
            {
                var working = staff.Where(e => !OnLeave(e, d) && e.HireDate <= d).ToList();
                var counter = 0;
                var transactions = new List<(DateTime At, bool Night)>();
                var (startH, endH) = d.DayOfWeek switch { DayOfWeek.Saturday => (9.0, 18.5), DayOfWeek.Sunday => (9.0, 21.0), _ => (8.5, 19.0) };
                var acute = d.DayOfWeek switch { DayOfWeek.Saturday => 44, DayOfWeek.Sunday => 30, DayOfWeek.Monday => 72, _ => 60 };
                var retail = d.DayOfWeek switch { DayOfWeek.Saturday => 58, DayOfWeek.Sunday => 26, _ => 52 };
                var dailyCount = (int)((acute + retail) * (0.9 + rnd.NextDouble() * 0.2));
                for (var k = 0; k < dailyCount; k++) transactions.Add((RandomTime(rnd, d, startH, endH), false));
                if (duty is { Kind: DutyKind.Night }) for (var k = 0; k < 24 + rnd.Next(10); k++) transactions.Add((RandomTime(rnd, d, 19.0, 23.9), true));
                var dueChronics = chronics.Where(c => c.Next <= d).ToList();
                var chronicTimes = dueChronics.Select(_ => RandomTime(rnd, d, startH, Math.Min(endH, 17))).ToList();

                var events = transactions.Select(t => (t.At, t.Night, Chronic: (Chronic?)null))
                    .Concat(dueChronics.Select((c, i) => (At: chronicTimes[i], Night: false, Chronic: (Chronic?)c)))
                    .OrderBy(e => e.At).ToList();
                foreach (var (at, night, chronic) in events)
                {
                    if (isToday && at > now) break;
                    var seller = night && duty?.Pharmacist is { } dp ? (rnd.Next(3) == 0 && duty.Assistant is { } da ? da : dp)
                        : working.Count == 0 ? owner : working[rnd.Next(working.Count)];
                    if (seller.Role == StaffRole.Intern && rnd.Next(4) != 0) seller = working.FirstOrDefault(e => e.Role == StaffRole.Technician) ?? owner;
                    var number = $"{d:yyMMdd}-{++counter:0000}";
                    Sale? sale = null;
                    try
                    {
                        if (chronic is not null)
                        {
                            sale = Sale.Prescription(number, at, chronic.Patient, chronic.Hospital, chronic.Branch, PrescriptionColor.White, RxNo(rnd),
                                d.AddDays(-rnd.Next(0, 3)), seller);
                            foreach (var p in chronic.Regimen) AddFefo(sale, p, p.Name.Contains("100 tablet") || p.Name.Contains("50 tablet") ? 1 : rnd.Next(1, 3), d, lots);
                            chronic.Next = d.AddDays(chronic.Interval + rnd.Next(-2, 3));
                        }
                        else if (rnd.NextDouble() < acute / (double)(acute + retail))
                        {
                            var (h, _, branches) = Pick(rnd, hospitals, x => x.Weight);
                            var branch = branches[rnd.Next(branches.Length)];
                            var groups = DemoCatalog.BranchGroups[branch];
                            var lines = Math.Clamp((int)Math.Round(Normal(rnd, branch == "Diş hekimliği" ? 1.6 : 2.2, 0.9)), 1, 4);
                            var chosen = new List<Product>();
                            for (var k = 0; k < lines * 2 && chosen.Count < lines; k++)
                            {
                                var pool = byGroup[groups[rnd.Next(groups.Length)]];
                                var p = Pick(rnd, pool, x => meta[x].Weight);
                                if (!chosen.Contains(p)) chosen.Add(p);
                            }
                            var colored = chosen.FirstOrDefault(p => p.Color != PrescriptionColor.White);
                            var color = colored?.Color ?? PrescriptionColor.White;
                            chosen = chosen.Where(p => p.Color == PrescriptionColor.White || p.Color == color).ToList();
                            var patient = patients[rnd.Next(patients.Count)];
                            if (branch == "Çocuk sağlığı") patient = patients.FirstOrDefault(x => x.AgeIn(d.Year) < 15 && rnd.Next(3) == 0) ?? patient;
                            var prescribed = d.AddDays(-(color == PrescriptionColor.White ? rnd.Next(0, 3) : rnd.Next(0, 2)));
                            sale = Sale.Prescription(number, at, patient, h, branch, color, RxNo(rnd), prescribed, seller);
                            foreach (var p in chosen) AddFefo(sale, p, rnd.NextDouble() < 0.8 ? 1 : 2, d, lots);
                        }
                        else
                        {
                            Patient? patient = rnd.NextDouble() < 0.15 ? patients[rnd.Next(patients.Count)] : null;
                            sale = Sale.Retail(number, at, seller, patient);
                            var n = rnd.NextDouble() switch { < 0.62 => 1, < 0.90 => 2, _ => 3 };
                            var chosen = new HashSet<Product>();
                            for (var k = 0; k < n * 2 && chosen.Count < n; k++) chosen.Add(Pick(rnd, retailProducts, x => meta[x].Weight));
                            foreach (var p in chosen) AddFefo(sale, p, rnd.NextDouble() < 0.85 ? 1 : 2, d, lots);
                        }
                        if (sale.Lines.Count == 0) { counter--; continue; }
                        var discount = sale.Kind == SaleKind.Retail && rnd.NextDouble() < 0.08 ? Math.Round(sale.Lines.Sum(l => l.LineTotal) * 0.05m, 2) : 0m;
                        sale.Complete(rnd.NextDouble() < 0.71 ? PaymentType.Card : PaymentType.Cash, discount);
                        sales.Add(sale);
                        foreach (var l in sale.Lines)
                            soldByDay[(l.Product, d)] = soldByDay.GetValueOrDefault((l.Product, d)) + l.Quantity;
                    }
                    catch (OnderCode.Kernel.Exceptions.DomainException)
                    {
                        counter--;
                    }
                }
                // Birkaç iade (dün ve öncesinden)
                if (!isToday && rnd.NextDouble() < 0.35 && sales.Count > 50)
                {
                    var victim = sales[^rnd.Next(5, 50)];
                    if (victim.Status == SaleStatus.Completed && victim.Kind == SaleKind.Retail) victim.Return(d.ToDateTime(new TimeOnly(17, 30)));
                }
            }

            // Sipariş: akşam (bugün ise sabah) stoğu azalanlar
            if (open || isToday)
            {
                var orderAt = isToday ? d.ToDateTime(new TimeOnly(Math.Clamp(now.Hour, 9, 11), 20)) : d.ToDateTime(new TimeOnly(18, 40));
                if (!isToday || orderAt <= now || now.Hour >= 9)
                {
                    var need = new List<(Product P, int Qty)>();
                    foreach (var p in products)
                    {
                        var usage = Usage(p, d);
                        var have = lots[p].Where(l => l.Expiry > d.AddDays(30)).Sum(l => l.Quantity);
                        var coming = orders.Where(o => o.Status == OrderStatus.Ordered).SelectMany(o => o.Lines).Where(l => l.Product == p).Sum(l => l.Quantity + l.FreeQuantity);
                        var qty = StockRules.SuggestOrder(usage, have, coming, p.MinStock, 1, 6);
                        if (qty > 0 && have + coming <= Math.Max(p.MinStock, usage * 3)) need.Add((p, Math.Max(qty, (int)Math.Ceiling(usage * 12))));
                    }
                    foreach (var g in need.GroupBy(n => ChooseWholesaler(n.P)))
                    {
                        var o = new PurchaseOrder($"SP-{d:yy}-{++orderSeq:00000}", g.Key, orderAt);
                        foreach (var (p, qty) in g)
                        {
                            var c = campaigns.FirstOrDefault(x => x.WholesalerId == g.Key.Id && x.Product == p && x.Wholesaler == g.Key && x.IsValid(d));
                            var q = c?.RoundUp(qty) ?? qty;
                            o.AddLine(p, q, Pricing.UnitPrice(p.SalePrice, p.Category, g.Key, p.Id), c?.FreeFor(q) ?? 0);
                        }
                        o.Place(orderAt);
                        orders.Add(o);
                    }
                }
            }
        }

        // ---- Kurum faturaları: biten aylar ertesi ayın 5'inde kesilir, SGK ~45 gün sonra öder ----
        var invoiceRows = new List<InstitutionInvoice>();
        var monthStart = new DateOnly(day0.Year, day0.Month, 1);
        for (var m = monthStart; m.AddMonths(1) <= today; m = m.AddMonths(1))
        {
            var issued = m.AddMonths(1).AddDays(4);
            if (issued > today) continue;
            foreach (var inst in new[] { Institution.Sgk, Institution.PrivateInsurance })
            {
                var ins = inst == Institution.Sgk ? InsuranceType.Sgk : InsuranceType.Private;
                var list = sales.Where(s => s.Status == SaleStatus.Completed && s.Kind == SaleKind.Prescription && s.Insurance == ins
                    && s.SoldAt.Year == m.Year && s.SoldAt.Month == m.Month && s.InstitutionShare > 0).ToList();
                if (list.Count == 0) continue;
                var inv = new InstitutionInvoice(inst, m.Year, m.Month, list.Sum(s => s.InstitutionShare), list.Count, issued);
                var paidOn = issued.AddDays(inst == Institution.Sgk ? 45 : 30);
                if (paidOn <= today) inv.MarkPaid(paidOn);
                invoiceRows.Add(inv);
            }
        }

        // ---- Demirbaşlar ----
        var assets = new List<Asset>
        {
            new("DM-001", "Raf sistemi (ön mağaza)", AssetCategory.Furniture, new DateOnly(2014, 9, 1), 186_000m, 10, "Ön mağaza", 8_000m),
            new("DM-002", "Raf sistemi (depo)", AssetCategory.Furniture, new DateOnly(2014, 9, 1), 64_000m, 10, "Depo", 2_000m),
            new("DM-003", "Satış bankosu", AssetCategory.Furniture, new DateOnly(2019, 6, 10), 92_000m, 10, "Ön mağaza", 3_000m),
            new("DM-004", "Dermokozmetik vitrini", AssetCategory.Furniture, new DateOnly(2023, 4, 3), 78_000m, 8, "Ön mağaza", 2_000m),
            new("DM-005", "İlaç soğutucu dolap 400 L", AssetCategory.ColdChain, new DateOnly(2021, 3, 15), 84_000m, 8, "Arka oda", 4_000m, 6, "SZ-48821"),
            new("DM-006", "İlaç soğutucu dolap 250 L (yedek)", AssetCategory.ColdChain, new DateOnly(2016, 8, 22), 38_000m, 8, "Depo", 1_000m, 6, "SZ-20914"),
            new("DM-007", "Sıcaklık izleme kaydedicisi", AssetCategory.ColdChain, new DateOnly(2024, 1, 8), 14_500m, 5, "Arka oda", 0m, 12, "TL-7720"),
            new("DM-008", "Kasa bilgisayarı 1", AssetCategory.Computer, new DateOnly(2023, 2, 14), 42_000m, 4, "Banko", 0m, 0, "PC-0231"),
            new("DM-009", "Kasa bilgisayarı 2", AssetCategory.Computer, new DateOnly(2023, 2, 14), 42_000m, 4, "Banko", 0m, 0, "PC-0232"),
            new("DM-010", "Arka ofis bilgisayarı", AssetCategory.Computer, new DateOnly(2020, 11, 5), 28_000m, 4, "Arka oda", 0m, 0, "PC-1105"),
            new("DM-011", "Karekod okuyucu (2 adet)", AssetCategory.Computer, new DateOnly(2024, 6, 3), 9_800m, 4, "Banko"),
            new("DM-012", "Etiket yazıcısı", AssetCategory.Computer, new DateOnly(2022, 9, 19), 7_400m, 4, "Arka oda"),
            new("DM-013", "Yazar kasa POS cihazı", AssetCategory.Computer, new DateOnly(2025, 1, 6), 18_600m, 5, "Banko", 0m, 12, "POS-55120"),
            new("DM-014", "Dijital tansiyon ölçüm cihazı (müşteri)", AssetCategory.Device, new DateOnly(2022, 5, 9), 6_800m, 5, "Ön mağaza", 0m, 12),
            new("DM-015", "Hassas terazi (majistral)", AssetCategory.Device, new DateOnly(2018, 3, 12), 12_400m, 10, "Laboratuvar", 400m, 12, "HT-3301"),
            new("DM-016", "Bebek tartısı", AssetCategory.Device, new DateOnly(2021, 10, 4), 4_900m, 5, "Ön mağaza", 0m, 12),
            new("DM-017", "Güvenlik kamera sistemi (8 kanal)", AssetCategory.Security, new DateOnly(2022, 7, 25), 36_000m, 5, "Genel", 0m, 12),
            new("DM-018", "Alarm sistemi", AssetCategory.Security, new DateOnly(2019, 2, 11), 18_000m, 5, "Genel", 0m, 12),
            new("DM-019", "Kepenk motoru", AssetCategory.Security, new DateOnly(2017, 9, 4), 14_000m, 8, "Giriş", 500m, 12),
            new("DM-020", "Klima (ön mağaza)", AssetCategory.Other, new DateOnly(2020, 6, 1), 48_000m, 8, "Ön mağaza", 2_000m, 6),
            new("DM-021", "Klima (depo)", AssetCategory.Other, new DateOnly(2016, 5, 16), 26_000m, 8, "Depo", 1_000m, 6),
            new("DM-022", "Nöbet tabelası (ışıklı)", AssetCategory.Other, new DateOnly(2015, 1, 12), 9_500m, 10, "Cephe", 0m),
            new("DM-023", "Eski kasa bilgisayarı", AssetCategory.Computer, new DateOnly(2016, 4, 4), 16_000m, 4, "Depo"),
            new("DM-024", "Majistral laboratuvar tezgahı", AssetCategory.Furniture, new DateOnly(2014, 9, 1), 22_000m, 15, "Laboratuvar", 1_000m),
        };
        assets.First(a => a.Code == "DM-023").Retire(new DateOnly(2023, 2, 14));
        var maintenance = new List<MaintenanceRecord>();
        foreach (var a in assets.Where(a => a.MaintenanceIntervalMonths > 0 && a.Status != AssetStatus.Retired))
        {
            // Son bakım: periyodun içinde bir tarih; birkaçının bakımı yaklaşsın/gecikmiş olsun
            var lastAgo = rnd.Next(1, a.MaintenanceIntervalMonths * 30 + 20);
            var date = today.AddDays(-lastAgo);
            if (date <= a.PurchaseDate) continue;
            var kind = a.Category == AssetCategory.Device || a.Code == "DM-007" ? MaintenanceKind.Calibration : MaintenanceKind.Maintenance;
            maintenance.Add(new MaintenanceRecord(a, date, kind, a.Category == AssetCategory.ColdChain ? 2_400m : kind == MaintenanceKind.Calibration ? 1_200m : 1_800m,
                kind == MaintenanceKind.Calibration ? "Kalibrasyon sertifikası alındı" : "Periyodik bakım"));
        }
        var frig = assets.First(a => a.Code == "DM-006");
        frig.SendToRepair();
        maintenance.Add(new MaintenanceRecord(assets.First(a => a.Code == "DM-019"), today.AddDays(-40), MaintenanceKind.Repair, 3_200m, "Motor kömürleri değişti"));

        db.Lots.AddRange(allLots);
        db.Movements.AddRange(movements);
        db.PurchaseOrders.AddRange(orders);
        db.SupplierPayments.AddRange(payments);
        db.Sales.AddRange(sales);
        db.Invoices.AddRange(invoiceRows);
        db.Assets.AddRange(assets);
        db.Maintenance.AddRange(maintenance);
        await db.SaveChangesAsync(cancellationToken);

        // ---- yerel yardımcılar ----
        double Usage(Product p, DateOnly d)
        {
            var span = Math.Min(14, d.DayNumber - day0.DayNumber);
            if (span < 7) return Expected(p);
            var sold = 0;
            for (var i = 1; i <= span; i++) sold += soldByDay.GetValueOrDefault((p, d.AddDays(-i)));
            return Math.Max(sold / (double)span, Expected(p) * 0.3);
        }

        Wholesaler ChooseWholesaler(Product p)
        {
            var withCampaign = campaigns.FirstOrDefault(c => c.Product == p && c.ValidUntil >= today.AddDays(-30));
            if (withCampaign is not null && rnd.NextDouble() < 0.7) return withCampaign.Wholesaler;
            var r = rnd.NextDouble();
            return r < 0.38 ? wholesalers[0] : r < 0.68 ? wholesalers[1] : r < 0.86 ? wholesalers[2] : wholesalers[3];
        }
    }

    private static void AddFefo(Sale sale, Product p, int qty, DateOnly day, Dictionary<Product, List<StockLot>> lots)
    {
        var available = lots[p].Where(l => l.Quantity > 0 && !l.IsExpired(day)).Sum(l => l.Quantity);
        if (available == 0) return; // stok yok: hasta başka eczaneye gider
        foreach (var (lot, take) in StockRules.Allocate(lots[p], Math.Min(qty, available), day))
            sale.AddLine(p, lot, take, day);
    }

    private static string RxNo(Random rnd) => string.Concat(Enumerable.Range(0, 7).Select(_ => RxChars[rnd.Next(RxChars.Length)]));

    private static DateTime RandomTime(Random rnd, DateOnly day, double fromHour, double toHour)
    {
        // Gün içinde iki tepe: öğleye doğru ve akşamüstü
        var u = rnd.NextDouble();
        var h = u < 0.45 ? Normal(rnd, fromHour + (toHour - fromHour) * 0.35, 1.4) : u < 0.85 ? Normal(rnd, fromHour + (toHour - fromHour) * 0.78, 1.2) : fromHour + rnd.NextDouble() * (toHour - fromHour);
        h = Math.Clamp(h, fromHour, toHour - 0.02);
        return day.ToDateTime(TimeOnly.MinValue).AddMinutes(Math.Round(h * 60));
    }

    private static double Normal(Random rnd, double mean, double sd)
    {
        var u1 = 1.0 - rnd.NextDouble();
        var u2 = rnd.NextDouble();
        return mean + sd * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private static T Pick<T>(Random rnd, IReadOnlyList<T> items, Func<T, double> weight)
    {
        var total = items.Sum(weight);
        var r = rnd.NextDouble() * total;
        foreach (var item in items)
        {
            r -= weight(item);
            if (r <= 0) return item;
        }
        return items[^1];
    }
}
