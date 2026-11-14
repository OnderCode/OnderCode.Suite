using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Sinema.Application;
using Sinema.Domain;

namespace Sinema.Web;

/// <summary>Bir tarayıcı bağlantısı (devre). Koltuk tutmalarında sahip kimliği olarak kullanılır.</summary>
public sealed class Visitor
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
}

/// <summary>Giriş yapmış personel (çerezden).</summary>
public sealed class CurrentStaff(AuthenticationStateProvider auth)
{
    public async Task<(int? Id, string? Name, StaffRole? Role)> GetAsync()
    {
        var state = await auth.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true) return (null, null, null);
        int? id = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var i) ? i : null;
        StaffRole? role = Enum.TryParse<StaffRole>(user.FindFirstValue(ClaimTypes.Role), out var r) ? r : null;
        return (id, user.Identity.Name, role);
    }
}

/// <summary>Süresi dolan koltuk tutmalarını düzenli aralıkla temizler (ekranlar anında güncellenir).</summary>
public sealed class HoldSweeper(SeatHoldService holds) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken)) holds.Sweep();
    }
}

public static class Roles
{
    public const string Manager = nameof(StaffRole.Manager);
    public const string BoxOffice = nameof(StaffRole.Manager) + "," + nameof(StaffRole.BoxOffice);
    public const string Usher = nameof(StaffRole.Manager) + "," + nameof(StaffRole.Usher);
}

/// <summary>Yönetim ekranlarında üst çubuktaki başlık ve düğmeler (sayfa <c>&lt;PageHeader&gt;</c> ile doldurur).</summary>
public sealed class PageInfo
{
    public string? Title { get; private set; }
    public string? Subtitle { get; private set; }
    public Microsoft.AspNetCore.Components.RenderFragment? Actions { get; private set; }

    public event Action? Changed;

    public void Set(string? title, string? subtitle, Microsoft.AspNetCore.Components.RenderFragment? actions)
    {
        Title = title;
        Subtitle = subtitle;
        Actions = actions;
        Changed?.Invoke();
    }
}
