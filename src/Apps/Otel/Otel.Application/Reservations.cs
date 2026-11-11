using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Queries;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using OnderCode.Kernel.Validation;
using Otel.Domain;

namespace Otel.Application;

// ---------- Müsait odalar + fiyat ----------

public sealed record AvailableRoom(int RoomId, string Number, int Floor, string RoomType, int Capacity, decimal BaseRate, decimal Total, decimal AverageNightly, decimal Discount)
{
    public string Display => $"{Number} · {RoomType}";
}

public sealed record FindAvailableRooms(DateOnly CheckIn, DateOnly CheckOut, int Adults) : IQuery<IReadOnlyList<AvailableRoom>>;

public sealed class FindAvailableRoomsValidator : AbstractValidator<FindAvailableRooms>
{
    public FindAvailableRoomsValidator()
    {
        RuleFor(x => x.CheckOut).GreaterThan(x => x.CheckIn).WithMessage("Çıkış tarihi giriş tarihinden sonra olmalı.");
        RuleFor(x => x.Adults).InclusiveBetween(1, 8).WithName("Kişi sayısı");
    }
}

public sealed class FindAvailableRoomsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<FindAvailableRooms, IReadOnlyList<AvailableRoom>>
{
    public async Task<Result<IReadOnlyList<AvailableRoom>>> HandleAsync(FindAvailableRooms q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var rooms = await uow.Repository<Room>().ListAsync(
            new Specification<Room>().Where(r => r.Status != RoomStatus.Maintenance && r.RoomType.Capacity >= q.Adults)
                .OrderBy(r => r.RoomType.BaseRate).OrderBy(r => r.Number),
            r => new { r.Id, r.Number, r.Floor, Type = r.RoomType.Name, r.RoomType.Capacity, r.RoomType.BaseRate },
            cancellationToken);

        var busy = (await uow.Repository<Reservation>().ListAsync(
            new Specification<Reservation>().Where(r =>
                (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.CheckedIn)
                && r.CheckIn < q.CheckOut && q.CheckIn < r.CheckOut),
            r => r.RoomId, cancellationToken)).ToHashSet();

        var seasons = await uow.Repository<Season>().ListAsync(cancellationToken: cancellationToken);
        var today = time.Today();

        IReadOnlyList<AvailableRoom> result = [.. rooms.Where(r => !busy.Contains(r.Id)).Select(r =>
        {
            var quote = PricingEngine.Quote(r.BaseRate, q.CheckIn, q.CheckOut, today, seasons, PricingPolicy.Default);
            return new AvailableRoom(r.Id, r.Number, r.Floor, r.Type, r.Capacity, r.BaseRate, quote.Total, quote.AverageNightly, quote.EarlyBookingDiscount);
        })];
        return Result<IReadOnlyList<AvailableRoom>>.Success(result);
    }
}

public sealed record QuoteStay(int RoomId, DateOnly CheckIn, DateOnly CheckOut) : IQuery<PriceQuote>;

public sealed class QuoteStayHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<QuoteStay, PriceQuote>
{
    public async Task<Result<PriceQuote>> HandleAsync(QuoteStay q, CancellationToken cancellationToken)
    {
        if (q.CheckOut <= q.CheckIn) return Error.Validation(nameof(q.CheckOut), "Çıkış tarihi giriş tarihinden sonra olmalı.");
        await using var uow = uowFactory.Create();
        var rate = (await uow.Repository<Room>().ListAsync(new Specification<Room>().Where(r => r.Id == q.RoomId), r => r.RoomType.BaseRate, cancellationToken))
            .Cast<decimal?>().FirstOrDefault() ?? throw new NotFoundException("Oda", q.RoomId);
        var seasons = await uow.Repository<Season>().ListAsync(cancellationToken: cancellationToken);
        return PricingEngine.Quote(rate, q.CheckIn, q.CheckOut, time.Today(), seasons, PricingPolicy.Default);
    }
}

// ---------- Yeni rezervasyon ----------

/// <summary>GuestId boşsa NewGuestName ile yeni misafir kartı açılır.</summary>
public sealed record CreateReservation(int? GuestId, string? NewGuestName, string? NewGuestIdentityNo, int RoomId,
    DateOnly CheckIn, DateOnly CheckOut, int Adults, string? Notes = null) : ICommand<int>;

public sealed class CreateReservationValidator : AbstractValidator<CreateReservation>
{
    public CreateReservationValidator(TimeProvider time)
    {
        RuleFor(x => x.RoomId).GreaterThan(0).WithMessage("Oda seçin.");
        RuleFor(x => x.CheckIn).GreaterThanOrEqualTo(_ => time.Today()).WithMessage("Geçmiş tarihe rezervasyon yapılamaz.");
        RuleFor(x => x.CheckOut).GreaterThan(x => x.CheckIn).WithMessage("Çıkış tarihi giriş tarihinden sonra olmalı.");
        RuleFor(x => x).Must(x => x.CheckOut.DayNumber - x.CheckIn.DayNumber <= 30)
            .WithName("Konaklama").WithMessage("Tek rezervasyonda en fazla 30 gece.").OverridePropertyName(nameof(CreateReservation.CheckOut));
        RuleFor(x => x.Adults).InclusiveBetween(1, 8).WithName("Kişi sayısı");
        RuleFor(x => x.NewGuestName).NotEmpty().When(x => x.GuestId is null).WithMessage("Misafir seçin ya da yeni misafirin adını yazın.")
            .MaximumLength(120);
        RuleFor(x => x.NewGuestIdentityNo)
            .Must(id => string.IsNullOrWhiteSpace(id) || id.Trim().Length != 11 || TurkishIdentity.IsValidTckn(id))
            .WithMessage("T.C. kimlik numarası geçerli değil.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CreateReservationHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<CreateReservation, int>
{
    public async Task<Result<int>> HandleAsync(CreateReservation c, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();
        // Çakışma kontrolü ve kayıt tek transaction'da: arada başka biri aynı odayı kapamasın
        return await uow.ExecuteInTransactionAsync(async ct =>
        {
            var room = await uow.Repository<Room>().FirstOrDefaultAsync(
                new Specification<Room>().Where(r => r.Id == c.RoomId).Include(r => r.RoomType).Tracking(), ct)
                ?? throw new NotFoundException("Oda", c.RoomId);

            var reservations = uow.Repository<Reservation>();
            var existing = await reservations.ListAsync(new Specification<Reservation>().Where(r =>
                r.RoomId == c.RoomId && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.CheckedIn)
                && r.CheckIn < c.CheckOut && c.CheckIn < r.CheckOut), ct);
            OverbookingRule.EnsureFree(room, c.CheckIn, c.CheckOut, existing);

            Guest guest;
            if (c.GuestId is int guestId)
            {
                guest = await uow.Repository<Guest>().GetRequiredAsync(guestId, ct);
            }
            else
            {
                guest = new Guest(c.NewGuestName!, c.NewGuestIdentityNo);
                uow.Repository<Guest>().Add(guest);
            }

            var seasons = await uow.Repository<Season>().ListAsync(cancellationToken: ct);
            var quote = PricingEngine.Quote(room.RoomType.BaseRate, c.CheckIn, c.CheckOut, today, seasons, PricingPolicy.Default);
            var code = await ReservationCodes.NextAsync(reservations, today, ct);

            var reservation = new Reservation(code, guest, room, c.CheckIn, c.CheckOut, c.Adults, quote.Total, today, c.Notes);
            reservations.Add(reservation);
            await uow.SaveChangesAsync(ct);
            return reservation.Id;
        }, cancellationToken);
    }
}

public static class ReservationCodes
{
    public static string Format(DateOnly date, int sequence) => $"R{date:yy}-{sequence:00000}";

    public static async Task<string> NextAsync(IRepository<Reservation> reservations, DateOnly today, CancellationToken ct)
    {
        var last = await reservations.ListAsync(new Specification<Reservation>().OrderByDescending(r => r.Id).Page(0, 1), r => r.Id, ct);
        return Format(today, (last.Count == 0 ? 0 : last[0]) + 1);
    }
}

// ---------- Giriş, çıkış, iptal, harcama ----------

public sealed record CheckIn(int ReservationId) : ICommand;

public sealed class CheckInHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<CheckIn>
{
    public async Task<Result> HandleAsync(CheckIn command, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var reservation = await ReservationLoader.LoadAsync(uow, command.ReservationId, cancellationToken);
        reservation.CheckInGuest(time.Today());
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record CheckOutSummary(string Code, string GuestName, string RoomNumber, int Nights, decimal RoomTotal, decimal ChargesTotal, decimal Balance, int LoyaltyPoints, string Tier);

public sealed record CheckOut(int ReservationId) : ICommand<CheckOutSummary>;

public sealed class CheckOutHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<CheckOut, CheckOutSummary>
{
    public async Task<Result<CheckOutSummary>> HandleAsync(CheckOut command, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var r = await ReservationLoader.LoadAsync(uow, command.ReservationId, cancellationToken);
        r.CheckOutGuest(PricingPolicy.Default.LoyaltyPointsPerNight);
        uow.Repository<HousekeepingTask>().Add(new HousekeepingTask(r.Room, TaskKind.Cleaning, $"Çıkış: {r.Code}"));
        await uow.SaveChangesAsync(cancellationToken);
        return new CheckOutSummary(r.Code, r.Guest.FullName, r.Room.Number, r.Nights, r.RoomTotal, r.ChargesTotal, r.Balance, r.Guest.LoyaltyPoints, Texts.Of(r.Guest.Tier));
    }
}

public sealed record CancelReservation(int ReservationId) : ICommand;

public sealed class CancelReservationHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<CancelReservation>
{
    public async Task<Result> HandleAsync(CancelReservation command, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var reservation = await ReservationLoader.LoadAsync(uow, command.ReservationId, cancellationToken);
        reservation.Cancel();
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record PostCharge(int ReservationId, ChargeCategory Category, string Description, decimal Amount) : ICommand;

public sealed class PostChargeValidator : AbstractValidator<PostCharge>
{
    public PostChargeValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(100).WithName("Açıklama");
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_000_000).WithName("Tutar");
    }
}

public sealed class PostChargeHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<PostCharge>
{
    public async Task<Result> HandleAsync(PostCharge command, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var reservation = await ReservationLoader.LoadAsync(uow, command.ReservationId, cancellationToken);
        reservation.PostCharge(command.Category, command.Description, command.Amount, time.GetUtcNow().UtcDateTime);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class ReservationLoader
{
    public static async Task<Reservation> LoadAsync(IUnitOfWork uow, int id, CancellationToken ct)
        => await uow.Repository<Reservation>().FirstOrDefaultAsync(
               new Specification<Reservation>().Where(r => r.Id == id)
                   .Include(r => r.Guest).Include(r => r.Room).Include("Room.RoomType").Include(r => r.Charges).Tracking(), ct)
           ?? throw new NotFoundException("Rezervasyon", id);
}

// ---------- Sorgular ----------

public sealed record ReservationRow(int Id, string Code, string GuestName, string RoomNumber, string RoomType,
    DateOnly CheckIn, DateOnly CheckOut, int Nights, int Adults, ReservationStatus Status, string StatusText, decimal RoomTotal, DateOnly BookedOn);

public sealed record ListReservations : PagedQuery<ReservationRow>
{
    public ReservationStatus? Status { get; init; }
}

public sealed class ListReservationsValidator : PagedQueryValidator<ListReservations, ReservationRow>
{
}

public sealed class ListReservationsHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<ListReservations, PagedResult<ReservationRow>>
{
    public async Task<Result<PagedResult<ReservationRow>>> HandleAsync(ListReservations q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var spec = new Specification<Reservation>()
            .Search(q.Search, r => r.Code, r => r.Guest.FullName, r => r.Room.Number)
            .OrderByDescending(r => r.CheckIn).OrderBy(r => r.Room.Number);
        if (q.Status is { } status) spec.Where(r => r.Status == status);

        var repo = uow.Repository<Reservation>();
        var total = await repo.CountAsync(spec, cancellationToken);
        var rows = await repo.ListAsync(spec.Page((q.Page - 1) * q.PageSize, q.PageSize),
            r => new { r.Id, r.Code, r.Guest.FullName, r.Room.Number, Type = r.Room.RoomType.Name, r.CheckIn, r.CheckOut, r.Adults, r.Status, r.RoomTotal, r.BookedOn },
            cancellationToken);

        var items = rows.Select(r => new ReservationRow(r.Id, r.Code, r.FullName, r.Number, r.Type, r.CheckIn, r.CheckOut,
            r.CheckOut.DayNumber - r.CheckIn.DayNumber, r.Adults, r.Status, Texts.Of(r.Status), r.RoomTotal, r.BookedOn)).ToList();
        return new PagedResult<ReservationRow>(items, total, q.Page, q.PageSize);
    }
}

public sealed record ChargeRow(DateTime PostedAt, string CategoryText, string Description, decimal Amount);

public sealed record ReservationDetail(int Id, string Code, int GuestId, string GuestName, string GuestTier, int GuestPoints,
    string RoomNumber, string RoomType, DateOnly CheckIn, DateOnly CheckOut, int Nights, int Adults,
    ReservationStatus Status, string StatusText, decimal RoomTotal, decimal ChargesTotal, decimal Balance, string? Notes,
    DateOnly BookedOn, IReadOnlyList<ChargeRow> Charges);

public sealed record GetReservationDetail(int ReservationId) : IQuery<ReservationDetail>;

public sealed class GetReservationDetailHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetReservationDetail, ReservationDetail>
{
    public async Task<Result<ReservationDetail>> HandleAsync(GetReservationDetail q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var r = await uow.Repository<Reservation>().FirstOrDefaultAsync(
            new Specification<Reservation>().Where(x => x.Id == q.ReservationId)
                .Include(x => x.Guest).Include(x => x.Room).Include("Room.RoomType").Include(x => x.Charges), cancellationToken)
            ?? throw new NotFoundException("Rezervasyon", q.ReservationId);

        var charges = r.Charges.OrderBy(c => c.PostedAt)
            .Select(c => new ChargeRow(c.PostedAt.ToLocalTime(), Texts.Of(c.Category), c.Description, c.Amount)).ToList();
        return new ReservationDetail(r.Id, r.Code, r.GuestId, r.Guest.FullName, Texts.Of(r.Guest.Tier), r.Guest.LoyaltyPoints,
            r.Room.Number, r.Room.RoomType.Name, r.CheckIn, r.CheckOut, r.Nights, r.Adults, r.Status, Texts.Of(r.Status),
            r.RoomTotal, r.ChargesTotal, r.Balance, r.Notes, r.BookedOn, charges);
    }
}

// ---------- Çizelge ----------

public sealed record TimelineRoom(int RoomId, string Number, int Floor, string RoomType, RoomStatus Status);

public sealed record TimelineBar(int ReservationId, int RoomId, string Code, string GuestName, DateOnly CheckIn, DateOnly CheckOut, ReservationStatus Status);

public sealed record TimelineView(DateOnly From, int Days, DateOnly Today, IReadOnlyList<TimelineRoom> Rooms, IReadOnlyList<TimelineBar> Bars);

public sealed record GetTimeline(DateOnly From, int Days) : IQuery<TimelineView>;

public sealed class GetTimelineHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetTimeline, TimelineView>
{
    public async Task<Result<TimelineView>> HandleAsync(GetTimeline q, CancellationToken cancellationToken)
    {
        var to = q.From.AddDays(Math.Clamp(q.Days, 1, 62));
        await using var uow = uowFactory.Create();
        var rooms = await uow.Repository<Room>().ListAsync(new Specification<Room>().OrderBy(r => r.Floor).OrderBy(r => r.Number),
            r => new TimelineRoom(r.Id, r.Number, r.Floor, r.RoomType.Name, r.Status), cancellationToken);
        var bars = await uow.Repository<Reservation>().ListAsync(
            new Specification<Reservation>().Where(r => r.Status != ReservationStatus.Cancelled && r.CheckIn < to && q.From < r.CheckOut),
            r => new TimelineBar(r.Id, r.RoomId, r.Code, r.Guest.FullName, r.CheckIn, r.CheckOut, r.Status), cancellationToken);
        return new TimelineView(q.From, to.DayNumber - q.From.DayNumber, time.Today(), rooms, bars);
    }
}
