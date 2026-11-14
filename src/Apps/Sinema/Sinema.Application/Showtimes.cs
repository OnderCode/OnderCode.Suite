using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Sinema.Domain;

namespace Sinema.Application;

/// <summary>Müşteri sitesindeki film kartı: film bilgisi ve seçilen günlerdeki seanslar.</summary>
public sealed record ProgramFilm(FilmRow Film, IReadOnlyList<SessionRow> Sessions);

public sealed record ProgramView(string CinemaName, string Branch, IReadOnlyList<ProgramFilm> NowShowing, IReadOnlyList<FilmRow> ComingSoon, FilmRow? Featured, int OnlineCloseMinutes);

/// <summary>Vizyon programı (müşteri sitesi). Online satışı kapanmış seanslar da gösterilir ama seçilemez.</summary>
public sealed record GetProgram(DateOnly From, int Days) : IQuery<ProgramView>;

public sealed class GetProgramHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetProgram, ProgramView>
{
    public async Task<Result<ProgramView>> HandleAsync(GetProgram q, CancellationToken cancellationToken)
    {
        var films = await new ListFilmsHandler(uowFactory, time).HandleAsync(new ListFilms(false), cancellationToken);
        await using var uow = uowFactory.Create();
        var settings = await SettingsQueries.GetAsync(uow, cancellationToken);
        var now = time.Now();
        var sessions = await SessionQueries.LoadAsync(uow, q.From.Start(), q.From.AddDays(q.Days).Start(), null, null, false, cancellationToken);
        var showing = films.Value.Where(f => f.Status == FilmStatus.NowShowing)
            .Select(f => new ProgramFilm(f, sessions.Where(s => s.FilmId == f.Id && s.End > now).ToList()))
            .OrderByDescending(p => p.Film.IsFeatured).ThenByDescending(p => p.Sessions.Sum(s => s.Sold)).ToList();
        var coming = films.Value.Where(f => f.Status == FilmStatus.ComingSoon).OrderBy(f => f.ReleaseDate).ToList();
        var featured = showing.Select(p => p.Film).FirstOrDefault(f => f.IsFeatured) ?? showing.Select(p => p.Film).FirstOrDefault();
        return new ProgramView(settings.Name, settings.Branch, showing, coming, featured, settings.OnlineSalesCloseMinutes);
    }
}

public sealed record FilmDetailView(FilmRow Film, IReadOnlyList<SessionRow> Sessions, int OnlineCloseMinutes);

public sealed record GetFilmDetail(int FilmId, int Days = 7) : IQuery<FilmDetailView>;

public sealed class GetFilmDetailHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetFilmDetail, FilmDetailView>
{
    public async Task<Result<FilmDetailView>> HandleAsync(GetFilmDetail q, CancellationToken cancellationToken)
    {
        var films = await new ListFilmsHandler(uowFactory, time).HandleAsync(new ListFilms(true), cancellationToken);
        var film = films.Value.FirstOrDefault(f => f.Id == q.FilmId);
        if (film is null) return Error.NotFound("Film bulunamadı.");
        await using var uow = uowFactory.Create();
        var settings = await SettingsQueries.GetAsync(uow, cancellationToken);
        var now = time.Now();
        var sessions = await SessionQueries.LoadAsync(uow, time.Today().Start(), time.Today().AddDays(q.Days).Start(), null, film.Id, false, cancellationToken);
        return new FilmDetailView(film, sessions.Where(s => s.End > now).ToList(), settings.OnlineSalesCloseMinutes);
    }
}

/// <summary>Personel girişi için aktif çalışanlar (demo: parola yok, kart seçerek girilir).</summary>
public sealed record ListLoginStaff : IQuery<IReadOnlyList<EmployeeRow>>;

public sealed class ListLoginStaffHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<ListLoginStaff, IReadOnlyList<EmployeeRow>>
{
    public async Task<Result<IReadOnlyList<EmployeeRow>>> HandleAsync(ListLoginStaff q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var staff = await uow.Repository<Employee>().ListAsync(new Specification<Employee>().Where(e => e.IsActive).OrderBy(e => e.Role).OrderBy(e => e.FullName), cancellationToken);
        return staff.Select(e => new EmployeeRow(e.Id, e.FullName, e.Role, e.HireDate, e.IsActive, e.Shift, 0, 0, 0)).ToList();
    }
}

public sealed record GetCinemaName : IQuery<(string Name, string Branch)>;

public sealed class GetCinemaNameHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetCinemaName, (string Name, string Branch)>
{
    public async Task<Result<(string Name, string Branch)>> HandleAsync(GetCinemaName q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var s = await SettingsQueries.GetAsync(uow, cancellationToken);
        return (s.Name, s.Branch);
    }
}
