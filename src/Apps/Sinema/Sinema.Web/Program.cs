using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using OnderCode.Application.Messaging;
using OnderCode.Data.Initialization;
using Onda.Web.Core;
using QRCoder;
using Sinema.Application;
using Sinema.Domain;
using Sinema.Infrastructure;
using Sinema.Web;
using Sinema.Web.Components;

var tr = CultureInfo.GetCultureInfo("tr-TR");
CultureInfo.DefaultThreadCurrentCulture = tr;
CultureInfo.DefaultThreadCurrentUICulture = tr;

var builder = WebApplication.CreateBuilder(args);
var uploads = Path.Combine(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"), "uploads");
builder.Services.AddSinema(uploads, o => o.DataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
builder.Services.AddOnda();
builder.Services.AddScoped<Visitor>();
builder.Services.AddScoped<CurrentStaff>();
builder.Services.AddScoped<PageInfo>();
builder.Services.AddHostedService<HoldSweeper>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/yonetim/giris";
    o.AccessDeniedPath = "/yonetim/yetki-yok";
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.Cookie.Name = "sinema.personel";
});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Veritabanı yoksa oluşturulur ve örnek veri yüklenir (kurulum yok)
await app.Services.GetRequiredService<DatabaseInitializer<SinemaDbContext>>().InitializeAsync();
await using (var scope = app.Services.CreateAsyncScope())
{
    var settings = await scope.ServiceProvider.GetRequiredService<ISender>().SendAsync(new GetSettings());
    if (settings.IsSuccess) app.Services.GetRequiredService<SeatHoldService>().HoldMinutes = settings.Value.HoldMinutes;
}

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/hata", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/bulunamadi");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapOnda();

// Demo personel girişi: parola yok, personel kartı seçilir.
app.MapPost("/giris", async (HttpContext http, [FromForm] int staffId, [FromForm] string? returnUrl, ISender sender) =>
{
    var staff = await sender.SendAsync(new ListLoginStaff());
    var e = staff.IsSuccess ? staff.Value.FirstOrDefault(x => x.Id == staffId) : null;
    return await SignInAsync(http, e, returnUrl);
});

// Yalnızca geliştirmede: ekran görüntüsü betiği için göreve göre tek adımda giriş (ör. /demo-giris/Manager?r=/yonetim).
if (app.Environment.IsDevelopment())
{
    app.MapGet("/demo-giris/{role}", async (HttpContext http, string role, string? r, ISender sender) =>
    {
        var staff = await sender.SendAsync(new ListLoginStaff());
        var e = staff.IsSuccess ? staff.Value.FirstOrDefault(x => string.Equals(x.Role.ToString(), role, StringComparison.OrdinalIgnoreCase)) : null;
        return await SignInAsync(http, e, r);
    });
}

static async Task<IResult> SignInAsync(HttpContext http, EmployeeRow? e, string? returnUrl)
{
    if (e is null) return Results.Redirect("/yonetim/giris");
    var identity = new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, e.Id.ToString(CultureInfo.InvariantCulture)),
        new Claim(ClaimTypes.Name, e.FullName),
        new Claim(ClaimTypes.Role, e.Role.ToString()),
    ], CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    var home = e.Role switch { StaffRole.BoxOffice => "/gise", StaffRole.Usher => "/kapi", _ => "/yonetim" };
    return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') ? home : returnUrl);
}

app.MapGet("/cikis", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/yonetim/giris");
});

// Bilet QR kodu (SVG). Kod yalnızca satış kodunu içerir; kişisel veri yoktur.
app.MapGet("/qr/{code}", (string code) =>
{
    var normalized = TicketCode.Normalize(code);
    if (normalized.Length != 9) return Results.NotFound();
    using var generator = new QRCodeGenerator();
    using var data = generator.CreateQrCode("SINEMA:" + normalized, QRCodeGenerator.ECCLevel.Q);
    var svg = new SvgQRCode(data).GetGraphic(8, "#111827", "#FFFFFF", drawQuietZones: true);
    return Results.Text(svg, "image/svg+xml");
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
