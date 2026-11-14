using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Sinema.Domain;

namespace Sinema.Application;

public sealed record FilmRow(int Id, string Title, string? OriginalTitle, string Genres, int DurationMinutes, AgeRating Rating, DateOnly ReleaseDate,
    string? Director, string? Cast, string? Country, string? Synopsis, string? PosterPath, string AccentColor, FilmStatus Status, bool IsFeatured,
    int UpcomingSessions, int TicketsSold, decimal Revenue)
{
    public string RatingText => Texts.Of(Rating);
    public string StatusText => Texts.Of(Status);
    public string Duration => $"{DurationMinutes / 60} sa {DurationMinutes % 60:00} dk";
    public string Poster => PosterPath ?? "posters/bos.svg";
}

public sealed record ListFilms(bool IncludeArchived = true) : IQuery<IReadOnlyList<FilmRow>>;

public sealed class ListFilmsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListFilms, IReadOnlyList<FilmRow>>
{
    public async Task<Result<IReadOnlyList<FilmRow>>> HandleAsync(ListFilms q, CancellationToken cancellationToken)
    {
        var now = time.Now();
        await using var uow = uowFactory.Create();
        var spec = new Specification<Film>().OrderBy(f => f.Status).OrderByDescending(f => f.ReleaseDate);
        if (!q.IncludeArchived) spec.Where(f => f.Status != FilmStatus.Archived);
        var films = await uow.Repository<Film>().ListAsync(spec, cancellationToken);
        var upcoming = await uow.Repository<Session>().ListAsync(new Specification<Session>().Where(s => s.Start >= now && !s.IsCancelled), s => s.FilmId, cancellationToken);
        var tickets = await uow.Repository<Ticket>().ListAsync(new Specification<Ticket>().Where(t => t.Status != TicketStatus.Refunded),
            t => new { t.Session!.FilmId, t.Price }, cancellationToken);
        var up = upcoming.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var sold = tickets.GroupBy(t => t.FilmId).ToDictionary(g => g.Key, g => (Count: g.Count(), Sum: g.Sum(x => x.Price)));
        return films.Select(f => new FilmRow(f.Id, f.Title, f.OriginalTitle, f.Genres, f.DurationMinutes, f.Rating, f.ReleaseDate, f.Director, f.Cast, f.Country,
            f.Synopsis, f.PosterPath, f.AccentColor, f.Status, f.IsFeatured, up.GetValueOrDefault(f.Id),
            sold.TryGetValue(f.Id, out var s) ? s.Count : 0, sold.TryGetValue(f.Id, out var s2) ? s2.Sum : 0)).ToList();
    }
}

public sealed record SaveFilm(int? Id, string Title, string? OriginalTitle, string Genres, int DurationMinutes, AgeRating Rating, DateOnly ReleaseDate,
    string? Director, string? Cast, string? Country, string? Synopsis, FilmStatus Status, bool IsFeatured, string AccentColor) : ICommand<int>;

public sealed class SaveFilmValidator : AbstractValidator<SaveFilm>
{
    public SaveFilmValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120).WithName("Film adı");
        RuleFor(x => x.Genres).NotEmpty().WithName("Tür");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(30, 300).WithName("Süre");
        RuleFor(x => x.Synopsis).MaximumLength(1500).WithName("Özet");
        RuleFor(x => x.Cast).MaximumLength(300).WithName("Oyuncular");
        RuleFor(x => x.AccentColor).Matches("^#[0-9A-Fa-f]{6}$").WithName("Vurgu rengi");
    }
}

public sealed class SaveFilmHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<SaveFilm, int>
{
    public async Task<Result<int>> HandleAsync(SaveFilm c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        Film f;
        if (c.Id is int id)
        {
            f = await uow.Repository<Film>().GetRequiredAsync(id, cancellationToken);
            if (f.DurationMinutes != c.DurationMinutes)
            {
                var now = time.Now();
                var hasFuture = await uow.Repository<Session>().AnyAsync(new Specification<Session>().Where(s => s.FilmId == id && !s.IsCancelled && s.Start > now), cancellationToken);
                if (hasFuture) return Error.Business("Bu filmin ileri tarihli seansları var; süreyi değiştirmek seans saatlerini bozar. Önce seansları iptal edin.");
            }
            f.Rename(c.Title, c.OriginalTitle);
            f.SetDetails(c.Genres, c.DurationMinutes, c.Rating, c.ReleaseDate);
        }
        else
        {
            f = new Film(c.Title, c.Genres, c.DurationMinutes, c.Rating, c.ReleaseDate);
            f.Rename(c.Title, c.OriginalTitle);
            uow.Repository<Film>().Add(f);
        }
        f.SetCredits(c.Director, c.Cast, c.Country, c.Synopsis);
        f.SetStatus(c.Status);
        f.SetFeatured(c.IsFeatured);
        f.SetAccent(c.AccentColor);
        await uow.SaveChangesAsync(cancellationToken);
        return f.Id;
    }
}

public sealed record SetFilmPoster(int FilmId, byte[] Content, string FileName) : ICommand<string>;

public sealed class SetFilmPosterHandler(IUnitOfWorkFactory uowFactory, IPosterStore store) : ICommandHandler<SetFilmPoster, string>
{
    static readonly string[] Allowed = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task<Result<string>> HandleAsync(SetFilmPoster c, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(c.FileName).ToLowerInvariant();
        if (!Allowed.Contains(ext)) return Error.Validation("Poster", "Afiş JPG, PNG ya da WEBP olmalı.");
        if (c.Content.Length is 0 or > 4 * 1024 * 1024) return Error.Validation("Poster", "Afiş en fazla 4 MB olabilir.");
        if (!LooksLikeImage(c.Content)) return Error.Validation("Poster", "Dosya bir resim gibi görünmüyor.");
        await using var uow = uowFactory.Create();
        var film = await uow.Repository<Film>().GetRequiredAsync(c.FilmId, cancellationToken);
        var path = await store.SaveAsync(film.Id, c.Content, ext, cancellationToken);
        film.SetPoster(path);
        await uow.SaveChangesAsync(cancellationToken);
        return path;
    }

    /// <summary>Uzantıya değil dosyanın ilk baytlarına bakar (JPEG, PNG, WEBP imzaları).</summary>
    internal static bool LooksLikeImage(byte[] b) =>
        b.Length > 12 && ((b[0] == 0xFF && b[1] == 0xD8) || (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            || (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P'));
}
