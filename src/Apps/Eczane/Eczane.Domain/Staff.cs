using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Eczane.Domain;

/// <summary>Eczanedeki görev. Nöbette en az bir eczacı bulunmak zorundadır.</summary>
public enum StaffRole { ResponsiblePharmacist, Pharmacist, Technician, Assistant, Intern }

/// <summary>
/// Eczane personeli. Kimlik numarası, telefon ve adres bilerek tutulmaz: eczanenin günlük işi için gerekmez,
/// kişisel veri en az tutulur.
/// </summary>
public sealed class Employee : FullAuditableEntity
{
    private Employee() { }

    public Employee(string fullName, StaffRole role, DateOnly hireDate, decimal monthlyGross)
    {
        Rename(fullName);
        Role = role;
        HireDate = hireDate;
        SetSalary(monthlyGross);
        IsActive = true;
    }

    public string FullName { get; private set; } = "";
    public StaffRole Role { get; private set; }
    public DateOnly HireDate { get; private set; }

    /// <summary>Aylık brüt ücret (personel maliyeti raporu için).</summary>
    public decimal MonthlyGross { get; private set; }

    public bool IsActive { get; private set; }
    public DateOnly? LeftOn { get; private set; }

    public bool IsPharmacist => Role is StaffRole.ResponsiblePharmacist or StaffRole.Pharmacist;

    public void Rename(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("Ad soyad boş olamaz.");
        FullName = fullName.Trim();
    }

    public void ChangeRole(StaffRole role) => Role = role;

    public void SetSalary(decimal monthlyGross)
    {
        if (monthlyGross < 0) throw new DomainException("Ücret negatif olamaz.");
        MonthlyGross = monthlyGross;
    }

    public void Leave(DateOnly on)
    {
        if (on < HireDate) throw new DomainException("Ayrılış tarihi işe giriş tarihinden önce olamaz.");
        IsActive = false;
        LeftOn = on;
    }

    /// <summary>Kıdem (tam yıl).</summary>
    public int YearsOfService(DateOnly asOf)
    {
        var years = asOf.Year - HireDate.Year;
        if (asOf < HireDate.AddYears(years)) years--;
        return Math.Max(0, years);
    }

    /// <summary>
    /// Yıllık ücretli izin hakkı (4857 sayılı İş Kanunu md. 53): 1-5 yıl 14 gün, 5-15 yıl 20 gün, 15 yıl ve üzeri 26 gün.
    /// Bir yılını doldurmamış çalışanın hakkı henüz doğmamıştır.
    /// </summary>
    public int AnnualLeaveEntitlement(DateOnly asOf) => YearsOfService(asOf) switch
    {
        < 1 => 0,
        < 5 => 14,
        < 15 => 20,
        _ => 26,
    };
}

public enum LeaveType { Annual, Sick, Excuse, Unpaid }

/// <summary>İzin kaydı. Gün sayısı pazar günleri hariç sayılır (eczane cumartesi açıktır).</summary>
public sealed class LeaveRecord : AuditableEntity
{
    private LeaveRecord() { }

    public LeaveRecord(Employee employee, LeaveType type, DateOnly start, DateOnly end, string? note = null)
    {
        if (end < start) throw new DomainException("İzin bitişi başlangıçtan önce olamaz.");
        if (end.DayNumber - start.DayNumber > 60) throw new DomainException("Tek izin kaydı 60 günü geçemez.");
        Employee = employee;
        EmployeeId = employee.Id;
        Type = type;
        Start = start;
        End = end;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public int EmployeeId { get; private set; }
    public Employee Employee { get; private set; } = null!;
    public LeaveType Type { get; private set; }
    public DateOnly Start { get; private set; }
    public DateOnly End { get; private set; }
    public string? Note { get; private set; }

    public int Days => WorkDays(Start, End);

    public bool Covers(DateOnly day) => day >= Start && day <= End;

    public bool Overlaps(DateOnly start, DateOnly end) => start <= End && end >= Start;

    /// <summary>Pazar hariç gün sayısı.</summary>
    public static int WorkDays(DateOnly start, DateOnly end)
    {
        var days = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
            if (d.DayOfWeek != DayOfWeek.Sunday) days++;
        return days;
    }
}
