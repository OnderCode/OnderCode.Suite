using Eczane.Domain;

namespace Eczane.Infrastructure;

/// <summary>
/// Örnek ürün listesi. İlaçlar etken madde, doz ve formla adlandırılır; marka adı yoktur. Fiyatlar örnektir.
/// Ağırlık, ürünün göreli satış sıklığıdır (kronik ilaçlar ve sık kullanılan reçetesizler yüksek).
/// </summary>
internal static class DemoCatalog
{
    public sealed record Item(string Name, string? Ingredient, string Group, ProductCategory Category, decimal Price, double Weight,
        PrescriptionColor Color = PrescriptionColor.White, bool Cold = false, bool Chronic = false, bool Reimbursed = true);

    static Item Rx(string name, string ingredient, string group, decimal price, double weight, bool chronic = false, PrescriptionColor color = PrescriptionColor.White,
        bool cold = false, bool reimbursed = true) => new(name, ingredient, group, ProductCategory.Rx, price, weight, color, cold, chronic, reimbursed);

    static Item Otc(string name, string? ingredient, string group, decimal price, double weight) => new(name, ingredient, group, ProductCategory.Otc, price, weight);
    static Item Dermo(string name, decimal price, double weight) => new(name, null, "Cilt bakımı", ProductCategory.Dermo, price, weight);
    static Item Supp(string name, decimal price, double weight) => new(name, null, "Takviye", ProductCategory.Supplement, price, weight);
    static Item Med(string name, decimal price, double weight) => new(name, null, "Medikal", ProductCategory.Medical, price, weight);
    static Item Baby(string name, decimal price, double weight) => new(name, null, "Anne-bebek", ProductCategory.Baby, price, weight);

    public static readonly Item[] Items =
    [
        // ---- Kardiyovasküler (kronik) ----
        Rx("Amlodipin 5 mg 30 tablet", "Amlodipin", "Kardiyovasküler", 64.90m, 9, true),
        Rx("Amlodipin 10 mg 30 tablet", "Amlodipin", "Kardiyovasküler", 92.40m, 6, true),
        Rx("Ramipril 5 mg 28 tablet", "Ramipril", "Kardiyovasküler", 118.60m, 5, true),
        Rx("Valsartan 160 mg 28 tablet", "Valsartan", "Kardiyovasküler", 156.30m, 6, true),
        Rx("Valsartan + hidroklorotiyazid 160/12,5 mg 28 tablet", "Valsartan, hidroklorotiyazid", "Kardiyovasküler", 172.80m, 5, true),
        Rx("Losartan 50 mg 28 tablet", "Losartan", "Kardiyovasküler", 98.70m, 4, true),
        Rx("Metoprolol süksinat 50 mg 30 tablet", "Metoprolol", "Kardiyovasküler", 104.20m, 6, true),
        Rx("Bisoprolol 5 mg 30 tablet", "Bisoprolol", "Kardiyovasküler", 86.50m, 5, true),
        Rx("Nebivolol 5 mg 28 tablet", "Nebivolol", "Kardiyovasküler", 138.90m, 4, true),
        Rx("Atorvastatin 20 mg 30 tablet", "Atorvastatin", "Kardiyovasküler", 132.40m, 8, true),
        Rx("Atorvastatin 40 mg 30 tablet", "Atorvastatin", "Kardiyovasküler", 184.60m, 6, true),
        Rx("Rosuvastatin 10 mg 28 tablet", "Rosuvastatin", "Kardiyovasküler", 162.30m, 5, true),
        Rx("Klopidogrel 75 mg 28 tablet", "Klopidogrel", "Kardiyovasküler", 214.80m, 4, true),
        Rx("Rivaroksaban 20 mg 28 tablet", "Rivaroksaban", "Kardiyovasküler", 1186.40m, 3, true),
        Rx("Apiksaban 5 mg 56 tablet", "Apiksaban", "Kardiyovasküler", 1342.70m, 2, true),
        Rx("Varfarin 5 mg 25 tablet", "Varfarin", "Kardiyovasküler", 58.20m, 2, true),
        Rx("Furosemid 40 mg 12 tablet", "Furosemid", "Kardiyovasküler", 34.60m, 3, true),
        Rx("İndapamid 1,5 mg 30 tablet", "İndapamid", "Kardiyovasküler", 96.10m, 3, true),
        Rx("Trimetazidin 35 mg 60 tablet", "Trimetazidin", "Kardiyovasküler", 148.50m, 3, true),
        // ---- Diyabet ----
        Rx("Metformin 1000 mg 100 tablet", "Metformin", "Diyabet", 128.40m, 9, true),
        Rx("Metformin 500 mg 100 tablet", "Metformin", "Diyabet", 86.20m, 4, true),
        Rx("Gliklazid 60 mg 30 tablet", "Gliklazid", "Diyabet", 112.70m, 4, true),
        Rx("Sitagliptin 100 mg 28 tablet", "Sitagliptin", "Diyabet", 486.30m, 4, true),
        Rx("Sitagliptin + metformin 50/1000 mg 56 tablet", "Sitagliptin, metformin", "Diyabet", 538.90m, 4, true),
        Rx("Empagliflozin 10 mg 30 tablet", "Empagliflozin", "Diyabet", 612.40m, 4, true),
        Rx("Dapagliflozin 10 mg 28 tablet", "Dapagliflozin", "Diyabet", 598.60m, 3, true),
        Rx("İnsülin glarjin 100 IU/ml 5 kalem", "İnsülin glarjin", "Diyabet", 1248.90m, 3, true, cold: true),
        Rx("İnsülin aspart 100 IU/ml 5 kalem", "İnsülin aspart", "Diyabet", 1096.40m, 2, true, cold: true),
        // ---- Tiroid ----
        Rx("Levotiroksin 50 mcg 50 tablet", "Levotiroksin", "Tiroid", 42.30m, 7, true),
        Rx("Levotiroksin 100 mcg 50 tablet", "Levotiroksin", "Tiroid", 48.90m, 5, true),
        Rx("Propiltiyourasil 50 mg 100 tablet", "Propiltiyourasil", "Tiroid", 96.80m, 1, true),
        // ---- Solunum ----
        Rx("Salbutamol 100 mcg inhaler 200 doz", "Salbutamol", "Solunum", 88.60m, 5, true),
        Rx("Budesonid + formoterol 160/4,5 mcg inhaler", "Budesonid, formoterol", "Solunum", 412.70m, 4, true),
        Rx("Flutikazon + salmeterol 250/50 mcg inhaler", "Flutikazon, salmeterol", "Solunum", 386.40m, 3, true),
        Rx("Tiotropyum 18 mcg 30 kapsül", "Tiotropyum", "Solunum", 462.10m, 2, true),
        Rx("Montelukast 10 mg 28 tablet", "Montelukast", "Solunum", 168.30m, 5, true),
        Rx("Montelukast 4 mg 28 çiğneme tableti", "Montelukast", "Çocuk", 142.60m, 3),
        Rx("Desloratadin 5 mg 20 tablet", "Desloratadin", "Solunum", 92.40m, 5),
        Rx("Levosetirizin 5 mg 20 tablet", "Levosetirizin", "Solunum", 84.70m, 4),
        Rx("Mometazon burun spreyi 50 mcg 140 doz", "Mometazon", "Solunum", 146.20m, 4),
        Rx("Ambroksol şurup 150 ml", "Ambroksol", "Solunum", 58.40m, 4),
        // ---- Antibiyotik ----
        Rx("Amoksisilin + klavulanik asit 1000 mg 14 tablet", "Amoksisilin, klavulanik asit", "Antibiyotik", 142.80m, 9),
        Rx("Sefuroksim aksetil 500 mg 14 tablet", "Sefuroksim", "Antibiyotik", 168.40m, 6),
        Rx("Azitromisin 500 mg 3 tablet", "Azitromisin", "Antibiyotik", 96.30m, 6),
        Rx("Klaritromisin 500 mg 14 tablet", "Klaritromisin", "Antibiyotik", 184.70m, 4),
        Rx("Siprofloksasin 500 mg 14 tablet", "Siprofloksasin", "Antibiyotik", 104.60m, 4),
        Rx("Levofloksasin 500 mg 7 tablet", "Levofloksasin", "Antibiyotik", 138.20m, 3),
        Rx("Sefiksim 400 mg 5 tablet", "Sefiksim", "Antibiyotik", 126.90m, 4),
        Rx("Fosfomisin 3 g 1 saşe", "Fosfomisin", "Antibiyotik", 112.30m, 3),
        Rx("Nitrofurantoin 100 mg 20 kapsül", "Nitrofurantoin", "Antibiyotik", 68.40m, 2),
        Rx("Metronidazol 500 mg 20 tablet", "Metronidazol", "Antibiyotik", 52.80m, 2),
        // ---- Çocuk ----
        Rx("Amoksisilin + klavulanik asit süspansiyon 457 mg/5 ml 70 ml", "Amoksisilin, klavulanik asit", "Çocuk", 118.60m, 6),
        Rx("Sefdinir süspansiyon 125 mg/5 ml 100 ml", "Sefdinir", "Çocuk", 134.20m, 4),
        Rx("Azitromisin süspansiyon 200 mg/5 ml 30 ml", "Azitromisin", "Çocuk", 92.70m, 3),
        Rx("Desloratadin şurup 0,5 mg/ml 150 ml", "Desloratadin", "Çocuk", 98.40m, 3),
        // ---- Mide-bağırsak ----
        Rx("Pantoprazol 40 mg 28 tablet", "Pantoprazol", "Mide-bağırsak", 124.60m, 9, true),
        Rx("Esomeprazol 40 mg 28 tablet", "Esomeprazol", "Mide-bağırsak", 168.20m, 5, true),
        Rx("Lansoprazol 30 mg 28 kapsül", "Lansoprazol", "Mide-bağırsak", 112.40m, 4, true),
        Rx("Famotidin 40 mg 30 tablet", "Famotidin", "Mide-bağırsak", 72.60m, 2),
        Rx("Trimebutin 200 mg 30 tablet", "Trimebutin", "Mide-bağırsak", 96.30m, 3),
        Rx("Domperidon 10 mg 30 tablet", "Domperidon", "Mide-bağırsak", 64.80m, 3),
        // ---- Analjezik ----
        Rx("Naproksen sodyum 550 mg 20 tablet", "Naproksen", "Analjezik", 88.40m, 6),
        Rx("Diklofenak sodyum 75 mg 20 tablet", "Diklofenak", "Analjezik", 62.30m, 5),
        Rx("Deksketoprofen 25 mg 20 tablet", "Deksketoprofen", "Analjezik", 96.70m, 6),
        Rx("Etodolak 400 mg 14 tablet", "Etodolak", "Analjezik", 104.20m, 3),
        Rx("Meloksikam 15 mg 10 tablet", "Meloksikam", "Analjezik", 58.60m, 3),
        // ---- Kas-iskelet ----
        Rx("Tiyokolşikosid 4 mg 20 kapsül", "Tiyokolşikosid", "Kas-iskelet", 112.80m, 5),
        Rx("Tiyokolşikosid 4 mg/2 ml 6 ampul", "Tiyokolşikosid", "Kas-iskelet", 86.40m, 2),
        Rx("Alendronat 70 mg 4 tablet", "Alendronat", "Kas-iskelet", 132.60m, 2, true),
        Rx("Kolşisin 0,5 mg 30 tablet", "Kolşisin", "Kas-iskelet", 54.20m, 2, true),
        // ---- Psikiyatri / Nöroloji ----
        Rx("Essitalopram 10 mg 28 tablet", "Essitalopram", "Psikiyatri", 142.30m, 5, true),
        Rx("Sertralin 50 mg 28 tablet", "Sertralin", "Psikiyatri", 118.60m, 5, true),
        Rx("Duloksetin 60 mg 28 kapsül", "Duloksetin", "Psikiyatri", 286.40m, 3, true),
        Rx("Ketiapin 25 mg 30 tablet", "Ketiapin", "Psikiyatri", 96.80m, 2, true),
        Rx("Alprazolam 0,5 mg 50 tablet", "Alprazolam", "Psikiyatri", 78.40m, 3, true, PrescriptionColor.Green),
        Rx("Klonazepam 2 mg 30 tablet", "Klonazepam", "Psikiyatri", 64.20m, 2, true, PrescriptionColor.Green),
        Rx("Metilfenidat 10 mg 30 tablet", "Metilfenidat", "Psikiyatri", 212.60m, 1, true, PrescriptionColor.Red),
        Rx("Pregabalin 75 mg 56 kapsül", "Pregabalin", "Nöroloji", 248.70m, 4, true),
        Rx("Gabapentin 300 mg 50 kapsül", "Gabapentin", "Nöroloji", 168.30m, 2, true),
        Rx("Levetirasetam 500 mg 50 tablet", "Levetirasetam", "Nöroloji", 312.40m, 2, true),
        Rx("Donepezil 10 mg 28 tablet", "Donepezil", "Nöroloji", 386.20m, 1, true),
        Rx("Sumatriptan 50 mg 2 tablet", "Sumatriptan", "Nöroloji", 88.60m, 2),
        // ---- Dermatoloji ----
        Rx("Fusidik asit krem %2 15 g", "Fusidik asit", "Dermatoloji", 92.40m, 4),
        Rx("Mometazon krem %0,1 30 g", "Mometazon", "Dermatoloji", 104.60m, 4),
        Rx("İzotretinoin 20 mg 30 kapsül", "İzotretinoin", "Dermatoloji", 486.20m, 2, false, reimbursed: false),
        Rx("Klindamisin jel %1 30 g", "Klindamisin", "Dermatoloji", 86.70m, 3),
        // ---- Üroloji ----
        Rx("Tamsulosin 0,4 mg 30 kapsül", "Tamsulosin", "Üroloji", 142.80m, 4, true),
        Rx("Solifenasin 5 mg 30 tablet", "Solifenasin", "Üroloji", 196.30m, 2, true),
        Rx("Finasterid 5 mg 28 tablet", "Finasterid", "Üroloji", 124.60m, 2, true),
        // ---- Kadın sağlığı ----
        Rx("Folik asit 5 mg 20 tablet", "Folik asit", "Kadın sağlığı", 32.40m, 3),
        Rx("Demir (III) hidroksit polimaltoz 100 mg 30 çiğneme tableti", "Demir", "Kadın sağlığı", 88.60m, 4),
        Rx("Progesteron 200 mg 30 kapsül", "Progesteron", "Kadın sağlığı", 312.40m, 2),
        // ---- Göz ----
        Rx("Latanoprost göz damlası 2,5 ml", "Latanoprost", "Göz", 214.60m, 2, true, cold: true),
        Rx("Tobramisin göz damlası 5 ml", "Tobramisin", "Göz", 72.30m, 3),
        Rx("Deksametazon + tobramisin göz damlası 5 ml", "Deksametazon, tobramisin", "Göz", 86.40m, 2),
        // ---- Vitamin-mineral (reçeteli) ----
        Rx("D3 vitamini 50.000 IU 4 kapsül", "Kolekalsiferol", "Vitamin-mineral", 92.80m, 5),
        Rx("B12 vitamini 1000 mcg 5 ampul", "Siyanokobalamin", "Vitamin-mineral", 68.40m, 3),
        Rx("Kalsiyum + D3 vitamini 30 efervesan tablet", "Kalsiyum, kolekalsiferol", "Vitamin-mineral", 104.20m, 3, true),
        // ---- Antiviral / antifungal ----
        Rx("Valasiklovir 500 mg 10 tablet", "Valasiklovir", "Antiviral-antifungal", 186.40m, 2),
        Rx("Flukonazol 150 mg 1 kapsül", "Flukonazol", "Antiviral-antifungal", 48.60m, 3),
        Rx("Oseltamivir 75 mg 10 kapsül", "Oseltamivir", "Antiviral-antifungal", 268.30m, 2),
        // ---- Biyolojik / özel ----
        Rx("Adalimumab 40 mg 2 kalem", "Adalimumab", "Biyolojik", 14864.20m, 0.15, true, cold: true),
        Rx("Teriparatid 20 mcg kalem", "Teriparatid", "Biyolojik", 6248.70m, 0.12, true, cold: true),
        Rx("Enoksaparin 6000 anti-Xa IU 10 enjektör", "Enoksaparin", "Kardiyovasküler", 1124.60m, 1.2),

        // ---- Reçetesiz ilaç ----
        Otc("Parasetamol 500 mg 20 tablet", "Parasetamol", "Ağrı-ateş", 38.40m, 14),
        Otc("Parasetamol 500 mg + kafein 65 mg 20 tablet", "Parasetamol, kafein", "Ağrı-ateş", 52.60m, 6),
        Otc("İbuprofen 400 mg 20 tablet", "İbuprofen", "Ağrı-ateş", 48.20m, 9),
        Otc("Asetilsalisilik asit 100 mg 30 tablet", "Asetilsalisilik asit", "Kardiyovasküler", 28.60m, 8),
        Otc("Parasetamol şurup 120 mg/5 ml 150 ml", "Parasetamol", "Çocuk", 46.80m, 6),
        Otc("İbuprofen süspansiyon 100 mg/5 ml 150 ml", "İbuprofen", "Çocuk", 58.40m, 6),
        Otc("Setirizin 10 mg 20 tablet", "Setirizin", "Alerji", 42.60m, 5),
        Otc("Loratadin 10 mg 20 tablet", "Loratadin", "Alerji", 46.30m, 3),
        Otc("Asetilsistein 600 mg 20 efervesan tablet", "Asetilsistein", "Soğuk algınlığı", 88.40m, 6),
        Otc("Soğuk algınlığı toz 10 saşe", "Parasetamol, fenilefrin", "Soğuk algınlığı", 96.70m, 6),
        Otc("Boğaz pastili 24 adet", "Flurbiprofen", "Soğuk algınlığı", 72.40m, 7),
        Otc("Deniz suyu burun spreyi 100 ml", null, "Soğuk algınlığı", 84.60m, 5),
        Otc("Simetikon damla 30 ml", "Simetikon", "Mide-bağırsak", 64.20m, 4),
        Otc("Antiasit çiğneme tableti 24 adet", "Kalsiyum karbonat, magnezyum", "Mide-bağırsak", 58.80m, 5),
        Otc("Oral rehidrasyon tozu 10 saşe", null, "Mide-bağırsak", 48.30m, 3),
        Otc("Bisakodil 5 mg 30 tablet", "Bisakodil", "Mide-bağırsak", 42.60m, 3),
        Otc("Diklofenak jel %1 50 g", "Diklofenak", "Kas-iskelet", 68.40m, 6),
        Otc("Terbinafin krem %1 15 g", "Terbinafin", "Cilt", 74.60m, 3),
        Otc("Suni gözyaşı damla 10 ml", "Sodyum hiyaluronat", "Göz", 96.40m, 5),
        Otc("Nikotin bandı 21 mg 7 adet", "Nikotin", "Bırakma desteği", 312.60m, 1.5),
        Otc("Yanık ve yara kremi 30 g", "Dekspantenol", "Cilt", 82.40m, 3),

        // ---- Dermokozmetik ----
        Dermo("Güneş koruyucu krem SPF 50+ 50 ml", 648.40m, 4),
        Dermo("Nemlendirici krem kuru cilt 200 ml", 486.20m, 4),
        Dermo("Hassas cilt temizleme jeli 200 ml", 412.60m, 4),
        Dermo("Akneye eğilimli cilt jeli 40 ml", 538.80m, 3),
        Dermo("Leke karşıtı serum 30 ml", 924.40m, 2),
        Dermo("Hyaluronik asit serum 30 ml", 786.30m, 2),
        Dermo("C vitamini serum 30 ml", 842.60m, 2),
        Dermo("Atopik cilt balsamı 400 ml", 694.80m, 2),
        Dermo("Saç dökülmesine karşı şampuan 400 ml", 568.40m, 3),
        Dermo("Kepek şampuanı 200 ml", 386.20m, 3),
        Dermo("Göz çevresi kremi 15 ml", 612.40m, 1.5),
        Dermo("Micellar temizleme suyu 400 ml", 452.80m, 3),
        Dermo("Dudak bakım kremi 15 ml", 168.40m, 3),
        Dermo("Topuk çatlak kremi 50 ml", 236.60m, 2),
        Dermo("Hassas diş macunu 75 ml", 148.20m, 4),
        Dermo("Antiseptik ağız gargarası 500 ml", 186.40m, 3),

        // ---- Gıda takviyesi ----
        Supp("Omega-3 balık yağı 60 kapsül", 486.40m, 4),
        Supp("D3 vitamini 1000 IU damla 20 ml", 268.60m, 5),
        Supp("Magnezyum 300 mg 30 tablet", 312.40m, 4),
        Supp("Multivitamin 30 tablet", 386.20m, 4),
        Supp("C vitamini 1000 mg 30 efervesan tablet", 186.80m, 5),
        Supp("Çinko 15 mg 30 tablet", 214.60m, 3),
        Supp("Probiyotik 10 kapsül", 248.40m, 4),
        Supp("Kolajen toz 30 saşe", 892.60m, 2),
        Supp("B kompleks vitamini 30 tablet", 236.20m, 3),
        Supp("Koenzim Q10 100 mg 30 kapsül", 568.40m, 1.5),
        Supp("Glukozamin + kondroitin 60 tablet", 624.80m, 2),
        Supp("Ekinezya 30 kapsül", 286.40m, 2),

        // ---- Medikal ----
        Med("Dijital tansiyon aleti koldan", 1486.40m, 0.6),
        Med("Temassız ateş ölçer", 924.60m, 0.6),
        Med("Kan şekeri ölçüm çubuğu 50 adet", 486.20m, 4),
        Med("Lanset 100 adet", 168.40m, 2),
        Med("Steril gazlı bez 10x10 cm 10 adet", 42.60m, 3),
        Med("Elastik bandaj 10 cm", 68.40m, 2),
        Med("Yara bandı 20 adet", 48.20m, 4),
        Med("Cerrahi maske 50 adet", 124.60m, 2),
        Med("Diz altı kompresyon çorabı", 612.40m, 0.8),
        Med("Kompresör nebülizatör", 1862.40m, 0.3),

        // ---- Anne-bebek ----
        Baby("Bebek maması 1 numara 400 g", 486.40m, 3),
        Baby("Bebek maması 2 numara 400 g", 498.60m, 3),
        Baby("Bebek pişik kremi 100 g", 196.40m, 3),
        Baby("Bebek şampuanı 200 ml", 168.20m, 2),
        Baby("Emzik 0-6 ay 2 adet", 186.40m, 1.5),
        Baby("Biberon 250 ml", 312.60m, 1.2),
        Baby("Islak mendil 56 adet", 54.80m, 3),
    ];

    /// <summary>Hekim branşının yazdığı tedavi grupları.</summary>
    public static readonly Dictionary<string, string[]> BranchGroups = new()
    {
        ["Aile hekimliği"] = ["Kardiyovasküler", "Diyabet", "Tiroid", "Mide-bağırsak", "Analjezik", "Solunum", "Vitamin-mineral", "Antibiyotik"],
        ["İç hastalıkları"] = ["Kardiyovasküler", "Diyabet", "Mide-bağırsak", "Tiroid", "Vitamin-mineral"],
        ["Kardiyoloji"] = ["Kardiyovasküler"],
        ["Çocuk sağlığı"] = ["Çocuk", "Solunum"],
        ["Göğüs hastalıkları"] = ["Solunum", "Antibiyotik"],
        ["Endokrinoloji"] = ["Diyabet", "Tiroid", "Vitamin-mineral"],
        ["Ortopedi"] = ["Kas-iskelet", "Analjezik"],
        ["Dermatoloji"] = ["Dermatoloji", "Antiviral-antifungal"],
        ["Kulak burun boğaz"] = ["Antibiyotik", "Solunum", "Analjezik"],
        ["Nöroloji"] = ["Nöroloji", "Analjezik"],
        ["Psikiyatri"] = ["Psikiyatri"],
        ["Göz hastalıkları"] = ["Göz"],
        ["Üroloji"] = ["Üroloji", "Antibiyotik"],
        ["Kadın doğum"] = ["Kadın sağlığı", "Vitamin-mineral", "Antibiyotik", "Antiviral-antifungal"],
        ["Genel cerrahi"] = ["Analjezik", "Antibiyotik", "Kardiyovasküler"],
        ["Diş hekimliği"] = ["Antibiyotik", "Analjezik"],
        ["Acil tıp"] = ["Analjezik", "Antibiyotik", "Mide-bağırsak"],
        ["Fizik tedavi"] = ["Kas-iskelet", "Analjezik"],
    };

    /// <summary>Örnek kurumlar (uydurma adlar), türü, göreli reçete ağırlığı ve branş dağılımı.</summary>
    public static readonly (string Name, HospitalKind Kind, double Weight, string[] Branches)[] Hospitals =
    [
        ("Merkez 1 No'lu Aile Sağlığı Merkezi", HospitalKind.FamilyHealth, 17, ["Aile hekimliği"]),
        ("Merkez 2 No'lu Aile Sağlığı Merkezi", HospitalKind.FamilyHealth, 12, ["Aile hekimliği"]),
        ("Çınaraltı Aile Sağlığı Merkezi", HospitalKind.FamilyHealth, 9, ["Aile hekimliği"]),
        ("Gökkuşağı Şehir Hastanesi", HospitalKind.City, 22,
            ["İç hastalıkları", "Kardiyoloji", "Çocuk sağlığı", "Göğüs hastalıkları", "Endokrinoloji", "Ortopedi", "Nöroloji", "Üroloji", "Genel cerrahi", "Acil tıp", "Fizik tedavi", "Göz hastalıkları"]),
        ("Yeşilvadi Devlet Hastanesi", HospitalKind.State, 14,
            ["İç hastalıkları", "Kardiyoloji", "Göğüs hastalıkları", "Ortopedi", "Kulak burun boğaz", "Göz hastalıkları", "Üroloji", "Acil tıp"]),
        ("Akdere Üniversitesi Tıp Fakültesi Hastanesi", HospitalKind.University, 7,
            ["Endokrinoloji", "Nöroloji", "Psikiyatri", "Dermatoloji", "Kardiyoloji"]),
        ("Mavikent Özel Hastanesi", HospitalKind.Private, 5, ["Dermatoloji", "Kadın doğum", "Ortopedi", "Çocuk sağlığı", "Kulak burun boğaz"]),
        ("Papatya Özel Tıp Merkezi", HospitalKind.Private, 3, ["Dermatoloji", "Psikiyatri", "Kadın doğum"]),
        ("Yeşilvadi Kadın Doğum ve Çocuk Hastanesi", HospitalKind.State, 6, ["Kadın doğum", "Çocuk sağlığı"]),
        ("Sahil Ağız ve Diş Sağlığı Merkezi", HospitalKind.Dental, 4, ["Diş hekimliği"]),
    ];

    public static readonly string[] FirstNamesF =
        ["Ayşe", "Fatma", "Zeynep", "Elif", "Merve", "Emine", "Hatice", "Selin", "Derya", "Gizem", "Esra", "Seda", "Gül", "Nermin", "Sevgi", "Hülya",
         "Aylin", "Ece", "İrem", "Büşra", "Kübra", "Melek", "Nazlı", "Yasemin", "Filiz", "Songül", "Sibel", "Tuğba", "Pınar", "Nur"];

    public static readonly string[] FirstNamesM =
        ["Mehmet", "Mustafa", "Ahmet", "Ali", "Hüseyin", "Hasan", "İbrahim", "Murat", "Emre", "Burak", "Can", "Kerem", "Oğuz", "Serkan", "Tolga", "Barış",
         "Onur", "Yusuf", "Kemal", "Halil", "Ramazan", "Osman", "Engin", "Cem", "Volkan", "Erkan", "Sinan", "Levent", "Umut", "Arda"];

    public static readonly string[] LastNames =
        ["Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Yıldız", "Öztürk", "Aydın", "Arslan", "Doğan", "Kılıç", "Aslan", "Çetin", "Koç", "Kurt",
         "Özdemir", "Polat", "Güneş", "Bulut", "Aksoy", "Ünal", "Tekin", "Erdem", "Kara", "Korkmaz", "Özkan", "Şimşek", "Avcı", "Taş", "Uçar",
         "Duman", "Yavuz", "Bozkurt", "Eren", "Sarı", "Güler", "Karaca", "Toprak", "Ateş", "Coşkun"];
}
