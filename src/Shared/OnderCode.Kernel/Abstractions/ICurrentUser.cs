namespace OnderCode.Kernel.Abstractions;

/// <summary>İşlemi yapan kullanıcı. Denetim alanlarına yazılır.</summary>
public interface ICurrentUser
{
    string? UserName { get; }
}

/// <summary>
/// Varsayılan kullanıcı: Windows oturum adı. Uygulama kendi girişini yaptığında <see cref="SignIn"/> ile değiştirilir.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private volatile string? _userName = Environment.UserName;

    public string? UserName => _userName;

    public void SignIn(string userName) => _userName = userName;

    public void SignOut() => _userName = Environment.UserName;
}
