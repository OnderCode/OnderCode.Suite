using OnderCode.Kernel.Domain;

namespace Otel.Domain;

public enum LoyaltyTier { Standard, Silver, Gold }

/// <summary>Misafir kartı ve sadakat puanı.</summary>
public sealed class Guest : FullAuditableEntity
{
    public const int SilverThreshold = 200;
    public const int GoldThreshold = 500;

    private Guest() { }

    public Guest(string fullName, string? identityNo = null, string? email = null, string? nationality = "TR")
    {
        FullName = fullName.Trim();
        IdentityNo = string.IsNullOrWhiteSpace(identityNo) ? null : identityNo.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Nationality = nationality;
    }

    public string FullName { get; private set; } = "";
    /// <summary>T.C. kimlik ya da pasaport numarası.</summary>
    public string? IdentityNo { get; private set; }
    public string? Email { get; private set; }
    public string? Nationality { get; private set; }
    public int LoyaltyPoints { get; private set; }
    public int StayCount { get; private set; }

    public LoyaltyTier Tier => TierOf(LoyaltyPoints);

    /// <summary>Puana göre seviye (sorgu projeksiyonlarında da kullanılır).</summary>
    public static LoyaltyTier TierOf(int points) => points >= GoldThreshold ? LoyaltyTier.Gold
        : points >= SilverThreshold ? LoyaltyTier.Silver
        : LoyaltyTier.Standard;

    /// <summary>Konaklama tamamlanınca: gece başına puan ve konaklama sayısı.</summary>
    public void CompleteStay(int nights, int pointsPerNight)
    {
        StayCount++;
        LoyaltyPoints += Math.Max(0, nights) * pointsPerNight;
    }
}
