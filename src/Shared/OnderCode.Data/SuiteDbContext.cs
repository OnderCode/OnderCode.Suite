using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using OnderCode.Data.Conventions;
using OnderCode.Kernel.Text;

namespace OnderCode.Data;

/// <summary>
/// Bütün uygulama bağlamlarının tabanı. Türetilen bağlam yalnızca DbSet'lerini tanımlar;
/// aynı derlemedeki IEntityTypeConfiguration sınıfları otomatik uygulanır. Kendiliğinden gelenler:
/// <list type="bullet">
/// <item>Türkçe sıralama ve büyük/küçük harf duyarsız eşitlik (bütün metin kolonları)</item>
/// <item>decimal alanlar INTEGER olarak, 4 hane hassasiyetle (SQL'de toplanabilir, sıralanabilir, kuruş kaybı yok)</item>
/// <item>Yumuşak silme filtresi, denetim alanları, sürüm numarasıyla eşzamanlılık kontrolü</item>
/// <item>Zincirleme silme kapalı: ilişkili kaydı olan satır silinemez (ERP için güvenli varsayılan)</item>
/// </list>
/// </summary>
public abstract class SuiteDbContext(DbContextOptions options) : DbContext(options)
{
    /// <summary>Metin kolonlarına uygulanan SQLite sıralama kuralının adı.</summary>
    public const string TurkishCollation = "TR";

    /// <summary>Türkçe arama fonksiyonunun SQLite'taki adı.</summary>
    public const string FoldFunction = "tr_fold";

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        ConfigureModel(modelBuilder);
        SuiteModelConventions.Apply(modelBuilder);

        modelBuilder.HasDbFunction(typeof(TurkishText).GetMethod(nameof(TurkishText.Fold), [typeof(string)])!)
            .HasName(FoldFunction)
            .IsBuiltIn();
    }

    /// <summary>Ek model ayarları için. Ortak kurallar bundan sonra uygulanır.</summary>
    protected virtual void ConfigureModel(ModelBuilder modelBuilder)
    {
    }

    /// <summary>Ezilirse base çağrılmalıdır.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Zincirleme silme yerine kısıtlama. Bir sipariş ve satırları gibi gerçek bütünlerde
        // .OnDelete(DeleteBehavior.Cascade) açıkça yazılır.
        configurationBuilder.Conventions.Remove(typeof(CascadeDeleteConvention));

        configurationBuilder.Properties<decimal>().HaveConversion<DecimalToScaledInt64Converter>();
        configurationBuilder.Properties<string>().HaveAnnotation(RelationalAnnotationNames.Collation, TurkishCollation);
    }
}
