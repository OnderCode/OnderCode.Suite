namespace OnderCode.Application.Results;

/// <summary>Kullanıcıya gösterilebilir hata. Field doluysa hangi alanla ilgili olduğunu söyler (ekranda o kutuyu kırmızı yapmak için).</summary>
public sealed record Error(string Code, string Message, string? Field = null)
{
    public static Error Validation(string field, string message) => new(ErrorCodes.Validation, message, field);
    public static Error NotFound(string message) => new(ErrorCodes.NotFound, message);
    public static Error Conflict(string message) => new(ErrorCodes.Concurrency, message);
    public static Error Business(string message) => new(ErrorCodes.Domain, message);
}

public static class ErrorCodes
{
    public const string Validation = "Validation";
    public const string NotFound = "NotFound";
    public const string Concurrency = "Concurrency";
    public const string Duplicate = "Duplicate";
    public const string InUse = "InUse";
    public const string Domain = "Domain";
}
