using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Sinema.Domain;

/// <summary>Film sınıflandırması (izleyici yaşı).</summary>
public enum AgeRating { General, SevenPlus, ThirteenPlus, SixteenPlus, EighteenPlus }

public enum FilmStatus { ComingSoon, NowShowing, Archived }

/// <summary>Film türleri sabit bir listeden seçilir; filmde virgülle ayrılmış olarak saklanır.</summary>
public static class Genres
{
    public static readonly string[] All =
    [
        "Aksiyon", "Animasyon", "Aile", "Belgesel", "Bilim kurgu", "Dram", "Fantastik",
        "Gerilim", "Komedi", "Korku", "Macera", "Müzikal", "Romantik", "Suç",
    ];
}

/// <summary>
/// Vizyondaki ya da vizyona girecek film. Afiş dosyası altyapıda saklanır; burada yalnızca yolu tutulur.
/// </summary>
public sealed class Film : FullAuditableEntity
{
    private Film() { }

    public Film(string title, string genres, int durationMinutes, AgeRating rating, DateOnly releaseDate)
    {
        Rename(title, null);
        SetDetails(genres, durationMinutes, rating, releaseDate);
        Status = FilmStatus.NowShowing;
        AccentColor = "#E11D48";
    }

    public string Title { get; private set; } = "";
    public string? OriginalTitle { get; private set; }
    public string Genres { get; private set; } = "";
    public int DurationMinutes { get; private set; }
    public AgeRating Rating { get; private set; }
    public DateOnly ReleaseDate { get; private set; }
    public string? Director { get; private set; }
    public string? Cast { get; private set; }
    public string? Synopsis { get; private set; }
    public string? Country { get; private set; }
    public string? PosterPath { get; private set; }
    /// <summary>Arayüzde filmin vurgu rengi (afişten seçilir).</summary>
    public string AccentColor { get; private set; } = "#E11D48";
    public FilmStatus Status { get; private set; }
    /// <summary>Ana sayfadaki büyük vitrinde gösterilir.</summary>
    public bool IsFeatured { get; private set; }

    public string Duration => $"{DurationMinutes / 60} sa {DurationMinutes % 60:00} dk";

    public void Rename(string title, string? originalTitle)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Film adı boş olamaz.");
        Title = title.Trim();
        OriginalTitle = string.IsNullOrWhiteSpace(originalTitle) ? null : originalTitle.Trim();
    }

    public void SetDetails(string genres, int durationMinutes, AgeRating rating, DateOnly releaseDate)
    {
        if (durationMinutes is < 30 or > 300) throw new DomainException("Film süresi 30 ile 300 dakika arasında olmalı.");
        var list = (genres ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(g => Domain.Genres.All.Contains(g)).Distinct().ToList();
        if (list.Count == 0) throw new DomainException("En az bir tür seçilmeli.");
        Genres = string.Join(", ", list);
        DurationMinutes = durationMinutes;
        Rating = rating;
        ReleaseDate = releaseDate;
    }

    public void SetCredits(string? director, string? cast, string? country, string? synopsis)
    {
        Director = Clean(director);
        Cast = Clean(cast);
        Country = Clean(country);
        Synopsis = Clean(synopsis);
    }

    public void SetPoster(string? path, string? accentColor = null)
    {
        PosterPath = path;
        if (!string.IsNullOrWhiteSpace(accentColor)) AccentColor = accentColor;
    }

    public void SetAccent(string color)
    {
        if (string.IsNullOrWhiteSpace(color) || color.Length != 7 || color[0] != '#') throw new DomainException("Renk #RRGGBB biçiminde olmalı.");
        AccentColor = color.ToUpperInvariant();
    }

    public void SetStatus(FilmStatus status) => Status = status;

    public void SetFeatured(bool featured) => IsFeatured = featured;

    /// <summary>Seans eklenebilir mi? Arşivdeki filme seans açılmaz.</summary>
    public bool CanSchedule => Status != FilmStatus.Archived;

    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
