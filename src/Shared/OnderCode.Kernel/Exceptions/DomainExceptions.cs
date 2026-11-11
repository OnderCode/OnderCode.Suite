namespace OnderCode.Kernel.Exceptions;

/// <summary>Kullanıcıya olduğu gibi gösterilebilecek iş kuralı hatası.</summary>
public class DomainException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class NotFoundException(string entityName, object key)
    : DomainException($"{entityName} kaydı bulunamadı (#{key}).")
{
    public string EntityName { get; } = entityName;
    public object Key { get; } = key;
}

/// <summary>Kayıt düzenlenirken başkası tarafından değiştirildi.</summary>
public sealed class ConcurrencyConflictException(Exception? innerException = null)
    : DomainException("Bu kayıt siz düzenlerken başka biri tarafından değiştirildi. Lütfen yenileyip tekrar deneyin.", innerException);

/// <summary>Benzersiz olması gereken bir bilgi (kod, plaka, TCKN...) zaten kayıtlı.</summary>
public sealed class DuplicateRecordException(Exception? innerException = null)
    : DomainException("Aynı bilgilerle kayıtlı başka bir kayıt var.", innerException);

/// <summary>Kayıt başka kayıtlarda kullanıldığı için silinemez/değiştirilemez.</summary>
public sealed class ReferenceInUseException(Exception? innerException = null)
    : DomainException("Bu kayıt başka kayıtlarda kullanıldığı için silinemez.", innerException);
