using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Sinema.Domain;

/// <summary>Görev: yetkiler buna göre belirlenir.</summary>
public enum StaffRole { Manager, BoxOffice, Usher }

/// <summary>Sinema personeli. Kimlik, telefon ve adres bilerek tutulmaz.</summary>
public sealed class Employee : FullAuditableEntity
{
    private Employee() { }

    public Employee(string fullName, StaffRole role, DateOnly hireDate)
    {
        Rename(fullName);
        Role = role;
        HireDate = hireDate;
        IsActive = true;
    }

    public string FullName { get; private set; } = "";
    public StaffRole Role { get; private set; }
    public DateOnly HireDate { get; private set; }
    public bool IsActive { get; private set; }
    /// <summary>Vardiya notu (ör. "Hafta içi akşam").</summary>
    public string? Shift { get; private set; }

    public void Rename(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("Ad soyad boş olamaz.");
        FullName = fullName.Trim();
    }

    public void ChangeRole(StaffRole role) => Role = role;

    public void SetShift(string? shift) => Shift = string.IsNullOrWhiteSpace(shift) ? null : shift.Trim();

    public void SetActive(bool active) => IsActive = active;
}
