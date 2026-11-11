# OnderCode Suite

.NET 10 + WinForms ile yazılmış, açık kaynak, uçtan uca çalışan otomasyon uygulamaları ve onların ortak altyapısı.
Kurulum gerektirmez: SQL Server yok, bağlantı ayarı yok. Uygulama ilk açılışta veritabanını oluşturur, migration'ları uygular ve örnek veriyi yükler.

**Lisans:** MIT. Ücretli ya da gelir sınırlı lisans isteyen hiçbir paket kullanılmaz.

## Çözüm yapısı

```
OnderCode.Suite/
├─ src/
│  ├─ Shared/
│  │  ├─ OnderCode.Kernel        Bağımlılıksız çekirdek: Entity tabanları, Specification,
│  │  │                          IRepository / IUnitOfWork sözleşmeleri, Türkçe metin yardımcıları
│  │  ├─ OnderCode.Data          EF Core + SQLite: SuiteDbContext, Repository, UnitOfWork,
│  │  │                          denetim, yumuşak silme, Türkçe sıralama/arama, başlatıcı
│  │  ├─ OnderCode.Application   Komut/sorgu dağıtıcı, davranış hattı (log, hata çevirisi,
│  │                          FluentValidation), Result türü, alan olayı dağıtıcısı
│  │  ├─ OnderCode.Reporting     (sırada) Excel / PDF / CSV, Pareto, trend ve tahminleme
│  │  └─ OnderCode.WinForms      (sırada) Onda bileşenleri üzerine uygulama kabuğu, DI, navigasyon
│  └─ Apps/                      (sırada) her uygulama 4 katman:
│     ├─ Otel/                     Otel.Domain · Otel.Application · Otel.Infrastructure · Otel.WinForms
│     ├─ Otopark/
│     ├─ Eczane/
│     ├─ SiteYonetimi/
│     ├─ Lojistik/
│     └─ Personel/
└─ tests/
   ├─ OnderCode.Data.Tests          Gerçek SQLite dosyası üzerinde veri katmanı testleri
   └─ OnderCode.Application.Tests   Dağıtıcı + hat + veri katmanı birlikte, uçtan uca
```

Bağımlılık yönü tek taraflıdır: `WinForms → Infrastructure → Application → Domain → Kernel`.
Kernel ve Data `net10.0` hedefler (Windows'a bağlı değildir); ileride aynı çekirdek web veya mobil arayüzle de kullanılabilir.

## Veri katmanı neler yapıyor?

| Konu | Yaklaşım |
|---|---|
| Sıfır kurulum | `%LocalAppData%\OnderCode\{Uygulama}\{Uygulama}.db`. Exe'nin yanında `portable` dosyası varsa veritabanı `Data\` klasöründe durur. |
| Şema | Migration varsa `Migrate()`, yoksa (prototip) `EnsureCreated()`. Başlangıç verisi `ISeeder` ile, tekrar çalışmaya dayanıklı. |
| DbContext ömrü | `IDbContextFactory` + kısa ömürlü `IUnitOfWork`. Her işlem kendi bağlamını açar ve kapatır; form boyunca açık kalan bağlamın getirdiği eski veri ve bellek sorunları olmaz. |
| Türkçe | Bütün metin kolonları Türk alfabesine göre sıralanır (Ç, C'den sonra; Iğdır, İzmir'den önce). Eşitlik ve benzersiz indeksler büyük/küçük harf duyarsızdır (`İST` = `ist`). `Search()` Türkçe karakter duyarsız arar (`sanliurfa` → Şanlıurfa) ve bu iş veritabanında yapılır. |
| Para | SQLite'ta decimal tür yok. Tutarlar 4 hane hassasiyetle tam sayı saklanır; toplama, sıralama ve karşılaştırma SQL'de, kuruş kaybı olmadan çalışır. |
| Denetim | `CreatedAt/By`, `UpdatedAt/By` otomatik ve UTC. Zaman `TimeProvider` ile alınır, testte sabitlenebilir. |
| Yumuşak silme | `ISoftDeletable` varlıklarda silme satırı işaretler; sorgular silinenleri kendiliğinden gizler, `WithDeleted()` ile görünür. |
| Eşzamanlılık | Her kayıtta artan `Version`. İki kişi aynı kaydı düzenlerse ikincisi anlaşılır bir uyarı alır. |
| Silme güvenliği | Zincirleme silme varsayılan olarak kapalı. İlişkili kaydı olan satır silinemez; gerçek bütünlerde (sipariş-satır) açıkça açılır. |
| Hatalar | SQLite kısıtlama hataları `DuplicateRecordException`, `ReferenceInUseException`, `ConcurrencyConflictException` gibi kullanıcıya gösterilebilir Türkçe istisnalara çevrilir. |
| Alan olayları | Varlıklar olay biriktirir; kayıt (varsa transaction commit'i) başarıyla bittikten sonra yayınlanır. |
| Performans | WAL modu, `synchronous=NORMAL`, liste sorguları varsayılan olarak izlenmez, ekran listeleri için projeksiyon. |

## Uygulama katmanı

Ekranlar yalnızca `ISender` ile konuşur; iş kuralları işleyicilerde, doğrulama doğrulayıcılarda durur.

| Parça | Görevi |
|---|---|
| `ICommand`, `ICommand<T>`, `IQuery<T>` | Ekranın gönderdiği mesajlar. Yanıt her zaman `Result` / `Result<T>`. |
| `ISender` | Mesajı işleyicisine ulaştırır. İstek türü başına bir kez yansıma, sonra önbellek. Her istek kendi DI kapsamında. |
| Davranış hattı | Dıştan içe: **Log** (süre, uyarı, hata) → **Alan istisnası çevirisi** (bulunamadı, mükerrer, eşzamanlılık → başarısız `Result`) → **Doğrulama** (FluentValidation, Türkçe mesajlar) → işleyici. |
| `Result` | Ekran try/catch yazmaz: `if (!sonuc.IsSuccess) form.HatalariGoster(sonuc.Errors)`. Her hatanın `Field` bilgisi var, ilgili kutu işaretlenebilir. |
| `IDomainEventHandler<T>` | Kayıt başarıyla bitince çalışır (ör. rezervasyon onaylandı → odayı dolu yap). |
| `TurkishRules` | `RuleFor(x => x.Tckn).NotEmpty().Tckn()`, `.Vkn()`, `.Iban()` |
| `PagedQuery<T>` | Liste ekranlarının ortak sorgusu: sayfa, sayfa boyutu, Türkçe arama. |

```csharp
public sealed record MusteriKaydet(string Ad, string Tckn) : ICommand<int>;

public sealed class MusteriKaydetDogrulayici : AbstractValidator<MusteriKaydet>
{
    public MusteriKaydetDogrulayici()
    {
        RuleFor(x => x.Ad).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Tckn).NotEmpty().Tckn();
    }
}

public sealed class MusteriKaydetIsleyici(IUnitOfWorkFactory uow) : ICommandHandler<MusteriKaydet, int>
{
    public async Task<Result<int>> HandleAsync(MusteriKaydet komut, CancellationToken ct)
    {
        await using var u = uow.Create();
        var musteri = new Musteri(komut.Ad, komut.Tckn);
        u.Repository<Musteri>().Add(musteri);
        await u.SaveChangesAsync(ct);
        return musteri.Id;
    }
}

// Ekranda
var sonuc = await sender.SendAsync(new MusteriKaydet(adKutusu.Text, tcknKutusu.Text));
```

## Veri katmanı kullanımı

```csharp
// Bağlam: yalnızca DbSet'ler. Aynı derlemedeki IEntityTypeConfiguration sınıfları otomatik uygulanır.
public sealed class OtelDbContext(DbContextOptions<OtelDbContext> options) : SuiteDbContext(options)
{
    public DbSet<Oda> Odalar { get; set; } = null!;
}

// Açılış
services.AddSuiteData<OtelDbContext>(o => o.AppName = "Otel")
        .AddSeeder<OtelDbContext, OdaSeeder>();
await provider.GetRequiredService<DatabaseInitializer<OtelDbContext>>().InitializeAsync();

// Okuma: Türkçe arama + sıralama + sayfalama, hepsi SQL'de
await using var uow = uowFactory.Create();
var sayfa = await uow.Repository<Oda>().PagedAsync(
    new Specification<Oda>().Search(arama, o => o.No, o => o.Aciklama).OrderBy(o => o.No),
    page: 1, pageSize: 50);

// Yazma
var oda = await uow.Repository<Oda>().GetRequiredAsync(id);
oda.TemizlikTamamlandi();
await uow.SaveChangesAsync();   // denetim, sürüm, olaylar kendiliğinden
```

## Bilinçli tercihler

- **MediatR ve AutoMapper yok.** İkisi de 2025'te ticari lisansa geçti. Komut/sorgu dağıtıcı küçük ve kendi kodumuz (`OnderCode.Application/Messaging`), eşleme elle ve açık.
- **FluentValidation var.** Apache 2.0 lisanslı, ücretsiz.
- **SQLite sürümü sabitlendi.** EF Core 10'un varsayılan olarak getirdiği yerel SQLite'ta yüksek önemli bir açık (CVE-2025-6965) var; `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 ile SQLite 3.53'e yükseltildi.
- **QuestPDF yok.** Belirli bir gelirin üstündeki şirketler için ücretli lisans istiyor. PDF için PDFsharp (MIT).
- **Arayüzde Onda.** Standart WinForms kontrolleri yerine [Onda](https://ondercode.com/projeler/onda) bileşenleri.
- **Generic repository, ama ince.** EF Core zaten bir iş birimi; buradaki katman onu gizlemek için değil, Application katmanını EF'den bağımsız tutmak ve ortak kuralları (Türkçe arama, sayfalama, hata çevirisi) tek yerde toplamak için var. Ağır rapor sorguları Infrastructure'da doğrudan DbContext ile yazılır.

## Derleme ve test

```
dotnet test OnderCode.Suite.slnx
```

ya da `test.bat` (sonuç `test.log` dosyasına yazılır). Gerekli olan tek şey .NET 10 SDK.

---
Abdurrahman Önder · [ondercode.com](https://ondercode.com)
