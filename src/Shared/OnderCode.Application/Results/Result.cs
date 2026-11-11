namespace OnderCode.Application.Results;

public interface IResult
{
    bool IsSuccess { get; }
    IReadOnlyList<Error> Errors { get; }
}

/// <summary>Hata listesinden kendi türünde başarısız sonuç üretebilen sonuç. Davranış hattı bunu kullanır.</summary>
public interface IResultType<TSelf> : IResult where TSelf : IResultType<TSelf>
{
    static abstract TSelf Failure(IReadOnlyList<Error> errors);
}

/// <summary>Değer döndürmeyen işlemin sonucu.</summary>
public sealed class Result : IResultType<Result>
{
    private static readonly Result Ok = new([]);

    private Result(IReadOnlyList<Error> errors) => Errors = errors;

    public IReadOnlyList<Error> Errors { get; }
    public bool IsSuccess => Errors.Count == 0;
    public bool IsFailure => !IsSuccess;
    /// <summary>Bütün hata mesajları alt alta (mesaj kutusunda göstermek için).</summary>
    public string ErrorMessage => string.Join(Environment.NewLine, Errors.Select(e => e.Message));

    public static Result Success() => Ok;

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result Failure(params Error[] errors) => Failure((IReadOnlyList<Error>)errors);

    public static Result Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0) throw new ArgumentException("Başarısız sonuç için en az bir hata gerekir.", nameof(errors));
        return new Result(errors);
    }

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Değer döndüren işlemin sonucu.</summary>
public sealed class Result<T> : IResultType<Result<T>>
{
    private readonly T? _value;

    private Result(T value, IReadOnlyList<Error> errors)
    {
        _value = value;
        Errors = errors;
    }

    public IReadOnlyList<Error> Errors { get; }
    public bool IsSuccess => Errors.Count == 0;
    public bool IsFailure => !IsSuccess;
    public string ErrorMessage => string.Join(Environment.NewLine, Errors.Select(e => e.Message));

    /// <summary>Başarısız sonuçta okunursa istisna fırlatır; önce <see cref="IsSuccess"/> kontrol edilmeli.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Başarısız sonucun değeri okunamaz: {ErrorMessage}");

    public static Result<T> Success(T value) => new(value, []);

    public static Result<T> Failure(params Error[] errors) => Failure((IReadOnlyList<Error>)errors);

    public static Result<T> Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0) throw new ArgumentException("Başarısız sonuç için en az bir hata gerekir.", nameof(errors));
        return new Result<T>(default!, errors);
    }

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
