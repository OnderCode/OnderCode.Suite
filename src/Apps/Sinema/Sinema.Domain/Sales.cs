using System.Security.Cryptography;
using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Sinema.Domain;

public enum SalesChannel { BoxOffice, Online }

public enum PaymentMethod { Cash, Card, Online }

public enum OrderStatus { Completed, Refunded }

public enum TicketStatus { Valid, Used, Refunded }

public enum ConcessionCategory { Popcorn, Drink, Snack, Menu }

/// <summary>Büfe ürünü.</summary>
public sealed class ConcessionItem : Entity
{
    private ConcessionItem() { }

    public ConcessionItem(string name, ConcessionCategory category, decimal price, int sortOrder = 0)
    {
        Update(name, category, price);
        SortOrder = sortOrder;
        IsActive = true;
    }

    public string Name { get; private set; } = "";
    public ConcessionCategory Category { get; private set; }
    public decimal Price { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, ConcessionCategory category, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Ürün adı boş olamaz.");
        if (price is <= 0 or > 10_000) throw new DomainException("Ürün fiyatı 0 ile 10.000 ₺ arasında olmalı.");
        Name = name.Trim();
        Category = category;
        Price = price;
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>
/// Bir satış: bir seansın biletleri ve/veya büfe ürünleri. Online satışlarda müşteri adı bilet üstünde görünür;
/// telefon, e-posta, kimlik gibi kişisel veri tutulmaz.
/// </summary>
public sealed class Order : AuditableEntity
{
    private readonly List<Ticket> _tickets = [];
    private readonly List<ConcessionLine> _concessions = [];

    private Order() { }

    public Order(string code, Session? session, SalesChannel channel, PaymentMethod payment, DateTime soldAt, string? customerName, int? soldById)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Satış kodu boş olamaz.");
        Code = code;
        SessionId = session?.Id;
        Session = session;
        Channel = channel;
        Payment = payment;
        SoldAt = soldAt;
        CustomerName = string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim();
        SoldById = soldById;
        Status = OrderStatus.Completed;
    }

    public string Code { get; private set; } = "";
    public int? SessionId { get; private set; }
    public Session? Session { get; private set; }
    public SalesChannel Channel { get; private set; }
    public PaymentMethod Payment { get; private set; }
    public DateTime SoldAt { get; private set; }
    public string? CustomerName { get; private set; }
    public int? SoldById { get; private set; }
    public Employee? SoldBy { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime? RefundedAt { get; private set; }
    public decimal TicketTotal { get; private set; }
    public decimal ConcessionTotal { get; private set; }
    public decimal Total { get; private set; }

    public IReadOnlyList<Ticket> Tickets => _tickets;
    public IReadOnlyList<ConcessionLine> Concessions => _concessions;

    public Ticket AddTicket(string seatCode, SeatType seatType, TicketType type, PriceQuote quote)
    {
        if (Session is null && SessionId is null) throw new DomainException("Bilet için seans gerekir.");
        if (_tickets.Any(t => string.Equals(t.SeatCode, seatCode, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException($"{seatCode} koltuğu bu satışta iki kez seçilmiş.");
        var ticket = new Ticket(this, seatCode, seatType, type, quote.Total);
        _tickets.Add(ticket);
        Recalculate();
        return ticket;
    }

    public void AddConcession(ConcessionItem item, int quantity)
    {
        if (quantity is <= 0 or > 50) throw new DomainException("Ürün adedi 1-50 arasında olmalı.");
        var existing = _concessions.FirstOrDefault(c => c.ItemId == item.Id && item.Id != 0);
        if (existing is not null) existing.Add(quantity);
        else _concessions.Add(new ConcessionLine(this, item, quantity));
        Recalculate();
    }

    public void Refund(DateTime at)
    {
        if (Status == OrderStatus.Refunded) throw new DomainException("Bu satış zaten iade edilmiş.");
        if (_tickets.Any(t => t.Status == TicketStatus.Used)) throw new DomainException("Kapıdan geçilmiş bileti olan satış iade edilemez.");
        foreach (var t in _tickets) t.MarkRefunded();
        Status = OrderStatus.Refunded;
        RefundedAt = at;
    }

    void Recalculate()
    {
        TicketTotal = _tickets.Sum(t => t.Price);
        ConcessionTotal = _concessions.Sum(c => c.Total);
        Total = TicketTotal + ConcessionTotal;
    }
}

public sealed class Ticket : Entity
{
    private Ticket() { }

    internal Ticket(Order order, string seatCode, SeatType seatType, TicketType type, decimal price)
    {
        Order = order;
        SessionId = order.SessionId ?? order.Session?.Id ?? 0;
        Session = order.Session;
        SeatCode = seatCode.ToUpperInvariant();
        SeatType = seatType;
        TicketTypeId = type.Id;
        TicketTypeName = type.Name;
        RequiresProof = type.RequiresProof;
        Price = price;
        Status = TicketStatus.Valid;
    }

    public int OrderId { get; private set; }
    public Order? Order { get; private set; }
    public int SessionId { get; private set; }
    public Session? Session { get; private set; }
    public string SeatCode { get; private set; } = "";
    public SeatType SeatType { get; private set; }
    public int TicketTypeId { get; private set; }
    public string TicketTypeName { get; private set; } = "";
    public bool RequiresProof { get; private set; }
    public decimal Price { get; private set; }
    public TicketStatus Status { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public int? CheckedById { get; private set; }

    public void CheckIn(DateTime at, int? usherId)
    {
        if (Status == TicketStatus.Refunded) throw new DomainException($"{SeatCode} bileti iade edilmiş.");
        if (Status == TicketStatus.Used) throw new DomainException($"{SeatCode} bileti {UsedAt:HH:mm}'de zaten kullanılmış.");
        Status = TicketStatus.Used;
        UsedAt = at;
        CheckedById = usherId;
    }

    internal void MarkRefunded() => Status = TicketStatus.Refunded;
}

public sealed class ConcessionLine : Entity
{
    private ConcessionLine() { }

    internal ConcessionLine(Order order, ConcessionItem item, int quantity)
    {
        Order = order;
        ItemId = item.Id;
        Item = item.Id == 0 ? item : null;
        Name = item.Name;
        Category = item.Category;
        UnitPrice = item.Price;
        Quantity = quantity;
    }

    public int OrderId { get; private set; }
    public Order? Order { get; private set; }
    public int ItemId { get; private set; }
    public ConcessionItem? Item { get; private set; }
    public string Name { get; private set; } = "";
    public ConcessionCategory Category { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Total => UnitPrice * Quantity;

    internal void Add(int quantity) => Quantity += quantity;
}

/// <summary>Bilet kodu: karıştırılması zor harf ve rakamlardan 8 karakter (ör. K7P2-QX9D).</summary>
public static class TicketCode
{
    const string Alphabet = "ABCDEFGHJKLMNPRSTUVYZ23456789";

    public static string New()
    {
        Span<char> c = stackalloc char[9];
        for (var i = 0; i < 9; i++) c[i] = i == 4 ? '-' : Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(c);
    }

    /// <summary>Kullanıcının yazdığı kodu (boşluk, tire, küçük harf) karşılaştırılabilir hale getirir.</summary>
    public static string Normalize(string? input)
    {
        var raw = new string((input ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return raw.Length == 8 ? raw[..4] + "-" + raw[4..] : raw;
    }
}

/// <summary>Kapı kontrol kuralları.</summary>
public static class CheckInRules
{
    /// <summary>Bilet şu an kapıdan okutulabilir mi? Değilse nedenini döndürür.</summary>
    public static string? WhyNot(Session session, DateTime now, CinemaSettings settings)
    {
        if (session.IsCancelled) return "Seans iptal edilmiş.";
        if (now < session.Start.AddMinutes(-settings.CheckInOpensMinutes))
            return $"Erken: kapı {session.Start.AddMinutes(-settings.CheckInOpensMinutes):HH:mm}'de açılır ({session.Start:dd.MM HH:mm} seansı).";
        if (now > session.End) return $"Seans bitmiş ({session.End:dd.MM HH:mm}).";
        return null;
    }
}
