using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Eczane.Domain;

public enum Gender { Female, Male }

/// <summary>Hastanın ödeme güvencesi.</summary>
public enum InsuranceType { Sgk, Private, None }

/// <summary>SGK'lı hastanın statüsü; katılım payı oranını belirler.</summary>
public enum SgkStatus { Worker, Retired }

/// <summary>
/// Hasta kartı. TC kimlik numarası, telefon ve adres bilerek tutulmaz: reçete karşılamak, ilaç geçmişini görmek ve
/// hangi hastaneden geldiğini analiz etmek için ad, doğum yılı ve güvence yeterlidir.
/// </summary>
public sealed class Patient : FullAuditableEntity
{
    private Patient() { }

    public Patient(string fullName, int birthYear, Gender gender, InsuranceType insurance, SgkStatus sgkStatus = SgkStatus.Worker,
        bool chronicReport = false, string? note = null)
    {
        Rename(fullName);
        if (birthYear is < 1900 or > 2100) throw new DomainException("Doğum yılı geçerli değil.");
        BirthYear = birthYear;
        Gender = gender;
        ChangeInsurance(insurance, sgkStatus, chronicReport);
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public string FullName { get; private set; } = "";
    public int BirthYear { get; private set; }
    public Gender Gender { get; private set; }
    public InsuranceType Insurance { get; private set; }
    public SgkStatus SgkStatus { get; private set; }

    /// <summary>Kronik hastalık raporu: raporlu ilaçlarda yüzde katılım payı alınmaz.</summary>
    public bool ChronicReport { get; private set; }

    public string? Note { get; private set; }

    public int AgeIn(int year) => Math.Max(0, year - BirthYear);

    public void Rename(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("Hasta adı boş olamaz.");
        FullName = fullName.Trim();
    }

    public void ChangeInsurance(InsuranceType insurance, SgkStatus sgkStatus, bool chronicReport)
    {
        Insurance = insurance;
        SgkStatus = sgkStatus;
        ChronicReport = insurance == InsuranceType.Sgk && chronicReport;
    }

    public void SetNote(string? note) => Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

public enum HospitalKind { State, City, University, Private, FamilyHealth, Dental }

/// <summary>Reçetenin yazıldığı sağlık kuruluşu. Örnek verideki kurum adları uydurmadır.</summary>
public sealed class Hospital : Entity
{
    private Hospital() { }

    public Hospital(string name, HospitalKind kind)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Kurum adı boş olamaz.");
        Name = name.Trim();
        Kind = kind;
    }

    public string Name { get; private set; } = "";
    public HospitalKind Kind { get; private set; }
}
