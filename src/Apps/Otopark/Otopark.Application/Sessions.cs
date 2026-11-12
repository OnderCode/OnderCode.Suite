using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Queries;
using OnderCode.Application.Results;
using OnderCode.Application.Validation;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using OnderCode.Kernel.Validation;
using Otopark.Domain;

namespace Otopark.Application;

// ---------- Giriş ----------

public sealed record EntryResult(int SessionId, string Plate, string SpotCode, bool IsSubscriber, string? SubscriberName, int? WashOrderId);

/// <summary>
/// Araç girişi. Yer verilmezse araç sınıfına en uygun boş yer seçilir. Abone plaka otomatik tanınır.
/// İstenirse aynı anda yıkama siparişi açılır; yıkama ücreti çıkışta otopark ücretiyle birlikte alınır.
/// </summary>
public sealed record RegisterEntry(string Plate, VehicleClass Vehicle, int? SpotId = null, int? WashPackageId = null, string? WashNote = null)
    : ICommand<EntryResult>;

public sealed class RegisterEntryValidator : AbstractValidator<RegisterEntry>
{
    public RegisterEntryValidator()
    {
        RuleFor(x => x.Plate).NotEmpty().Plate().WithName("Plaka");
        RuleFor(x => x.Vehicle).IsInEnum();
        RuleFor(x => x.WashNote).MaximumLength(200);
    }
}

public sealed class RegisterEntryHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<RegisterEntry, EntryResult>
{
    public async Task<Result<EntryResult>> HandleAsync(RegisterEntry c, CancellationToken cancellationToken)
    {
        var plate = TurkishPlate.Normalize(c.Plate)!;
        var now = time.Now();
        await using var uow = uowFactory.Create();
        // Plaka ve yer kontrolü kayıtla aynı transaction'da: iki gişe aynı yeri aynı anda veremesin
        return await uow.ExecuteInTransactionAsync(async ct =>
        {
            var sessions = uow.Repository<ParkingSession>();
            var inside = await sessions.FirstOrDefaultAsync(
                new Specification<ParkingSession>().Where(s => s.Plate == plate && s.Status == SessionStatus.Active).Include(s => s.Spot), ct);
            if (inside is not null) throw new DomainException($"{plate} zaten içeride ({inside.Spot.Code}, giriş {inside.EntryAt:HH:mm}).");

            ParkingSpot spot;
            if (c.SpotId is int spotId)
            {
                spot = await uow.Repository<ParkingSpot>().FirstOrDefaultAsync(new Specification<ParkingSpot>().Where(s => s.Id == spotId).Tracking(), ct)
                       ?? throw new NotFoundException("Park yeri", spotId);
                var taken = await sessions.AnyAsync(new Specification<ParkingSession>().Where(s => s.SpotId == spotId && s.Status == SessionStatus.Active), ct);
                if (taken) throw new DomainException($"{spot.Code} dolu.");
            }
            else
            {
                var free = await SpotFinder.FreeSpotsAsync(uow, c.Vehicle, ct, tracking: true);
                spot = free.FirstOrDefault() ?? throw new DomainException($"{Texts.Of(c.Vehicle)} için boş yer yok.");
            }

            var today = DateOnly.FromDateTime(now);
            var subscription = await uow.Repository<Subscription>().FirstOrDefaultAsync(
                new Specification<Subscription>().Where(s => s.Plate == plate && s.StartDate <= today && s.EndDate >= today).Tracking(), ct);

            var session = new ParkingSession(plate, c.Vehicle, spot, now, subscription);
            sessions.Add(session);

            WashOrder? wash = null;
            if (c.WashPackageId is int packageId)
            {
                var package = await uow.Repository<WashPackage>().FirstOrDefaultAsync(new Specification<WashPackage>().Where(p => p.Id == packageId).Tracking(), ct)
                              ?? throw new NotFoundException("Yıkama paketi", packageId);
                wash = new WashOrder(plate, c.Vehicle, package, now, session, c.WashNote);
                uow.Repository<WashOrder>().Add(wash);
            }
            await uow.SaveChangesAsync(ct);
            return Result<EntryResult>.Success(new EntryResult(session.Id, plate, spot.Code, subscription is not null, subscription?.OwnerName, wash?.Id));
        }, cancellationToken);
    }
}

// ---------- Çıkış ----------

public sealed record WashLine(int OrderId, string PackageName, WashStatus Status, decimal Price)
{
    public string StatusText => Texts.Of(Status);
}

public sealed record ExitQuote(
    int SessionId, string Plate, string SpotCode, VehicleClass Vehicle, DateTime EntryAt, DateTime Now,
    ParkingFee Fee, IReadOnlyList<WashLine> Washes, bool IsSubscriber, string? SubscriberName)
{
    public decimal WashTotal => Washes.Where(w => w.Status != WashStatus.Cancelled).Sum(w => w.Price);
    public decimal GrandTotal => Fee.Total + WashTotal;
    public string DurationText => Texts.Duration(Fee.Duration);
    public string VehicleText => Texts.Of(Vehicle);

    /// <summary>Yıkaması bitmemiş araç çıkamaz.</summary>
    public string? BlockReason => Washes.FirstOrDefault(w => w.Status is WashStatus.Waiting or WashStatus.Washing) is { } w
        ? $"Yıkama bitmedi: {w.PackageName} ({w.StatusText.ToLower(Texts.Turkish)})"
        : null;
}

public sealed record QuoteExit(int SessionId) : IQuery<ExitQuote>;

public sealed class QuoteExitHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<QuoteExit, ExitQuote>
{
    public async Task<Result<ExitQuote>> HandleAsync(QuoteExit q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var (session, washes) = await ExitData.LoadAsync(uow, q.SessionId, tracking: false, cancellationToken);
        var lines = washes.Select(w => new WashLine(w.Id, w.Package.Name, w.Status, w.Price)).ToList();
        var fee = session.Quote(time.Now(), lines.Any(w => w.Status != WashStatus.Cancelled));
        return new ExitQuote(session.Id, session.Plate, session.Spot.Code, session.Vehicle, session.EntryAt, session.ExitAt ?? time.Now(),
            fee, lines, session.SubscriptionId is not null, session.Subscription?.OwnerName);
    }
}

public sealed record ExitReceipt(int SessionId, string Plate, string SpotCode, DateTime EntryAt, DateTime ExitAt, ParkingFee Fee, decimal WashFee, PaymentMethod Payment)
{
    public decimal Total => Fee.Total + WashFee;
    public string PaymentText => Texts.Of(Payment);
    public string DurationText => Texts.Duration(ExitAt - EntryAt);
}

/// <summary>Çıkış ve tahsilat. Bağlı yıkama siparişi hazırsa teslim edilmiş sayılır ve ücreti fişe eklenir.</summary>
public sealed record RegisterExit(int SessionId, PaymentMethod Payment) : ICommand<ExitReceipt>;

public sealed class RegisterExitHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<RegisterExit, ExitReceipt>
{
    public async Task<Result<ExitReceipt>> HandleAsync(RegisterExit c, CancellationToken cancellationToken)
    {
        var now = time.Now();
        await using var uow = uowFactory.Create();
        var (session, washes) = await ExitData.LoadAsync(uow, c.SessionId, tracking: true, cancellationToken);
        var billable = washes.Where(w => w.Status != WashStatus.Cancelled).ToList();
        foreach (var w in billable) w.DeliverWithExit(now);
        var washFee = billable.Sum(w => w.Price);
        var fee = session.Close(now, c.Payment, washFee, billable.Count > 0);
        await uow.SaveChangesAsync(cancellationToken);
        return new ExitReceipt(session.Id, session.Plate, session.Spot.Code, session.EntryAt, now, fee, washFee, session.Payment ?? c.Payment);
    }
}

internal static class ExitData
{
    public static async Task<(ParkingSession Session, IReadOnlyList<WashOrder> Washes)> LoadAsync(IUnitOfWork uow, int sessionId, bool tracking, CancellationToken ct)
    {
        var spec = new Specification<ParkingSession>().Where(s => s.Id == sessionId).Include(s => s.Spot).Include(s => s.Subscription);
        if (tracking) spec.Tracking();
        var session = await uow.Repository<ParkingSession>().FirstOrDefaultAsync(spec, ct) ?? throw new NotFoundException("Araç kaydı", sessionId);
        var washSpec = new Specification<WashOrder>().Where(w => w.SessionId == sessionId).Include(w => w.Package).OrderBy(w => w.OrderedAt);
        if (tracking) washSpec.Tracking();
        var washes = await uow.Repository<WashOrder>().ListAsync(washSpec, ct);
        return (session, washes);
    }
}

/// <summary>Plakaya göre içerideki aracı bulur (gişede plaka yazıp çıkış).</summary>
public sealed record FindActiveSession(string Plate) : IQuery<int?>;

public sealed class FindActiveSessionHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<FindActiveSession, int?>
{
    public async Task<Result<int?>> HandleAsync(FindActiveSession q, CancellationToken cancellationToken)
    {
        var plate = TurkishPlate.Normalize(q.Plate);
        if (plate is null) return Result<int?>.Success(null);
        await using var uow = uowFactory.Create();
        var ids = await uow.Repository<ParkingSession>().ListAsync(
            new Specification<ParkingSession>().Where(s => s.Plate == plate && s.Status == SessionStatus.Active), s => s.Id, cancellationToken);
        return Result<int?>.Success(ids.Count == 0 ? null : ids[0]);
    }
}

// ---------- Araç hareketleri ----------

public enum SessionFilter { Inside, Today, All }

public sealed record SessionRow(int Id, string Plate, string VehicleText, string SpotCode, DateTime EntryAt, DateTime? ExitAt,
    string DurationText, decimal ParkingFee, decimal WashFee, decimal Total, string StatusText, string? PaymentText, bool IsSubscriber);

public sealed record ListSessions : PagedQuery<SessionRow>
{
    public SessionFilter Filter { get; init; } = SessionFilter.Today;
}

public sealed class ListSessionsValidator : PagedQueryValidator<ListSessions, SessionRow>
{
}

public sealed class ListSessionsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListSessions, PagedResult<SessionRow>>
{
    public async Task<Result<PagedResult<SessionRow>>> HandleAsync(ListSessions q, CancellationToken cancellationToken)
    {
        var now = time.Now();
        var dayStart = now.Date;
        await using var uow = uowFactory.Create();
        var spec = new Specification<ParkingSession>().OrderByDescending(s => s.EntryAt);
        switch (q.Filter)
        {
            case SessionFilter.Inside: spec.Where(s => s.Status == SessionStatus.Active); break;
            case SessionFilter.Today: spec.Where(s => s.EntryAt >= dayStart || s.ExitAt >= dayStart || s.Status == SessionStatus.Active); break;
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            // Plaka aramada boşluk önemsiz: "34abc" → "34 ABC" ile eşleşsin
            var compact = string.Concat(q.Search.Where(ch => !char.IsWhiteSpace(ch))).ToUpperInvariant();
            spec.Where(s => s.Plate.Replace(" ", "").Contains(compact));
        }
        var total = await uow.Repository<ParkingSession>().CountAsync(spec, cancellationToken);
        spec.Page((q.Page - 1) * q.PageSize, q.PageSize);
        var rows = await uow.Repository<ParkingSession>().ListAsync(spec,
            s => new { s.Id, s.Plate, s.Vehicle, s.Spot.Code, s.EntryAt, s.ExitAt, s.ParkingFee, s.WashFee, s.Status, s.Payment, s.SubscriptionId },
            cancellationToken);
        // İçerideki araçlar için o anki tutar: tarife + bağlı yıkama siparişleri
        var activeIds = rows.Where(r => r.Status == SessionStatus.Active).Select(r => r.Id).ToList();
        var openWashes = await uow.Repository<WashOrder>().ListAsync(
            new Specification<WashOrder>().Where(w => w.SessionId != null && activeIds.Contains(w.SessionId.Value) && w.Status != WashStatus.Cancelled),
            w => new { w.SessionId, w.Price }, cancellationToken);
        var washBySession = openWashes.GroupBy(w => w.SessionId!.Value).ToDictionary(g => g.Key, g => g.Sum(w => w.Price));
        var items = rows.Select(s =>
        {
            var parking = s.ParkingFee;
            var wash = s.WashFee;
            if (s.Status == SessionStatus.Active)
            {
                wash = washBySession.GetValueOrDefault(s.Id);
                parking = TariffEngine.Calculate(Tariff.For(s.Vehicle), s.EntryAt, now, washBySession.ContainsKey(s.Id), s.SubscriptionId is not null).Total;
            }
            return new SessionRow(s.Id, s.Plate, Texts.Of(s.Vehicle), s.Code, s.EntryAt, s.ExitAt,
                Texts.Duration((s.ExitAt ?? now) - s.EntryAt), parking, wash, parking + wash, Texts.Of(s.Status),
                s.Payment is { } p ? Texts.Of(p) : null, s.SubscriptionId is not null);
        }).ToList();
        return new PagedResult<SessionRow>(items, total, q.Page, q.PageSize);
    }
}
