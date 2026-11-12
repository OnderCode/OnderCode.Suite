using Eczane.Domain;
using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace Eczane.Application;

// ---------- Hastalar ----------

public sealed record PatientRow(int Id, string FullName, int BirthYear, Gender Gender, InsuranceType Insurance, SgkStatus SgkStatus, bool ChronicReport,
    int Prescriptions, int Purchases, decimal Spent, DateTime? LastVisit, string? TopHospital, string? Note, int Age)
{
    public string GenderText => Texts.Of(Gender);
    public string InsuranceText => Insurance == InsuranceType.Sgk ? $"SGK · {Texts.Of(SgkStatus).ToLower(Texts.Turkish)}" : Texts.Of(Insurance);
    public string ChronicText => ChronicReport ? "Raporlu" : "";
}

public sealed record ListPatients(string? Search = null) : IQuery<IReadOnlyList<PatientRow>>;

public sealed class ListPatientsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListPatients, IReadOnlyList<PatientRow>>
{
    public async Task<Result<IReadOnlyList<PatientRow>>> HandleAsync(ListPatients q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var patients = await uow.Repository<Patient>().ListAsync(new Specification<Patient>().OrderBy(p => p.FullName).Search(q.Search, p => p.FullName, p => p.Note), cancellationToken);
        var sales = await uow.Repository<Sale>().ListAsync(new Specification<Sale>().Where(s => s.PatientId != null && s.Status == SaleStatus.Completed),
            s => new { PatientId = s.PatientId!.Value, s.Kind, s.SoldAt, s.PatientShare, Hospital = s.Hospital!.Name }, cancellationToken);
        var byPatient = sales.GroupBy(s => s.PatientId).ToDictionary(g => g.Key, g => g.ToList());
        var year = time.Today().Year;
        return patients.Select(p =>
        {
            var mine = byPatient.GetValueOrDefault(p.Id) ?? [];
            var top = mine.Where(s => s.Hospital is not null).GroupBy(s => s.Hospital).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
            return new PatientRow(p.Id, p.FullName, p.BirthYear, p.Gender, p.Insurance, p.SgkStatus, p.ChronicReport,
                mine.Count(s => s.Kind == SaleKind.Prescription), mine.Count(s => s.Kind == SaleKind.Retail), mine.Sum(s => s.PatientShare),
                mine.Count == 0 ? null : mine.Max(s => s.SoldAt), top, p.Note, p.AgeIn(year));
        }).ToList();
    }
}

/// <summary>Hasta ekle / düzenle. Bilerek TC kimlik no, telefon ve adres alanı yoktur.</summary>
public sealed record SavePatient(int? Id, string FullName, int BirthYear, Gender Gender, InsuranceType Insurance, SgkStatus SgkStatus, bool ChronicReport, string? Note)
    : ICommand<int>;

public sealed class SavePatientValidator : AbstractValidator<SavePatient>
{
    public SavePatientValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100).WithName("Ad soyad");
        RuleFor(x => x.BirthYear).InclusiveBetween(1910, DateTime.Today.Year).WithName("Doğum yılı");
        RuleFor(x => x.Note).MaximumLength(300).WithName("Not");
    }
}

public sealed class SavePatientHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<SavePatient, int>
{
    public async Task<Result<int>> HandleAsync(SavePatient c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        Patient p;
        if (c.Id is int id)
        {
            p = await uow.Repository<Patient>().GetRequiredAsync(id, cancellationToken);
            p.Rename(c.FullName);
            p.ChangeInsurance(c.Insurance, c.SgkStatus, c.ChronicReport);
            p.SetNote(c.Note);
        }
        else
        {
            p = new Patient(c.FullName, c.BirthYear, c.Gender, c.Insurance, c.SgkStatus, c.ChronicReport, c.Note);
            uow.Repository<Patient>().Add(p);
        }
        await uow.SaveChangesAsync(cancellationToken);
        return p.Id;
    }
}

public sealed record PatientMedication(string Product, int Boxes, DateTime LastAt, int Times, double? AverageIntervalDays)
{
    /// <summary>Düzenli kullanılan ilaçta bir sonraki alım günü tahmini.</summary>
    public DateOnly? NextExpected => AverageIntervalDays is { } d && Times >= 3 ? DateOnly.FromDateTime(LastAt).AddDays((int)Math.Round(d)) : null;
}

public sealed record PatientHistory(PatientRow Patient, IReadOnlyList<SaleRow> Sales, IReadOnlyList<PatientMedication> Medications);

/// <summary>Hastanın geçmişi ve düzenli kullandığı ilaçlar (ortalama alım aralığıyla bir sonraki alım tahmini).</summary>
public sealed record GetPatientHistory(int PatientId) : IQuery<PatientHistory>;

public sealed class GetPatientHistoryHandler(IUnitOfWorkFactory uowFactory, ISender sender) : IQueryHandler<GetPatientHistory, PatientHistory>
{
    public async Task<Result<PatientHistory>> HandleAsync(GetPatientHistory q, CancellationToken cancellationToken)
    {
        var patients = await sender.SendAsync(new ListPatients(), cancellationToken);
        var row = patients.Value.FirstOrDefault(p => p.Id == q.PatientId) ?? throw new NotFoundException("Hasta", q.PatientId);
        var sales = await sender.SendAsync(new ListSales { Period = SalePeriod.All, PatientId = q.PatientId, PageSize = 200 }, cancellationToken);
        await using var uow = uowFactory.Create();
        var lines = await uow.Repository<SaleLine>().ListAsync(
            new Specification<SaleLine>().Where(l => l.Sale.PatientId == q.PatientId && l.Sale.Status == SaleStatus.Completed),
            l => new { l.Product.Name, l.Quantity, l.Sale.SoldAt }, cancellationToken);
        var meds = lines.GroupBy(l => l.Name).Select(g =>
        {
            var days = g.Select(x => DateOnly.FromDateTime(x.SoldAt)).Distinct().OrderBy(d => d).ToList();
            double? interval = days.Count >= 2 ? (days[^1].DayNumber - days[0].DayNumber) / (double)(days.Count - 1) : null;
            return new PatientMedication(g.Key, g.Sum(x => x.Quantity), g.Max(x => x.SoldAt), days.Count, interval);
        }).OrderByDescending(m => m.Times).ThenByDescending(m => m.LastAt).ToList();
        return new PatientHistory(row, sales.Value.Items, meds);
    }
}

// ---------- Hastaneler ----------

public sealed record HospitalRow(int Id, string Name, HospitalKind Kind, int Prescriptions, int Patients, decimal Revenue, decimal InstitutionShare,
    double Share, int Last30, int Previous30, string? TopBranch)
{
    public string KindText => Texts.Of(Kind);
    public double? Growth => Previous30 == 0 ? null : (Last30 - Previous30) / (double)Previous30;
    public decimal AverageRevenue => Prescriptions == 0 ? 0 : Revenue / Prescriptions;
}

/// <summary>Kurumlara göre reçete sayısı, ciro ve son 30 günün önceki 30 güne göre değişimi.</summary>
public sealed record ListHospitals(int Days = 90) : IQuery<IReadOnlyList<HospitalRow>>;

public sealed class ListHospitalsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListHospitals, IReadOnlyList<HospitalRow>>
{
    public async Task<Result<IReadOnlyList<HospitalRow>>> HandleAsync(ListHospitals q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var from = today.AddDays(-q.Days + 1).Start();
        var last30 = today.AddDays(-29).Start();
        var prev30 = today.AddDays(-59).Start();
        await using var uow = uowFactory.Create();
        var hospitals = await uow.Repository<Hospital>().ListAsync(cancellationToken: cancellationToken);
        var start = from < prev30 ? from : prev30;
        var rx = await uow.Repository<Sale>().ListAsync(
            new Specification<Sale>().Where(s => s.Kind == SaleKind.Prescription && s.Status == SaleStatus.Completed && s.SoldAt >= start),
            s => new { HospitalId = s.HospitalId!.Value, s.SoldAt, s.PatientId, s.PatientShare, s.InstitutionShare, s.Branch }, cancellationToken);
        var inPeriod = rx.Where(s => s.SoldAt >= from).ToList();
        var total = inPeriod.Count;
        return hospitals.Select(h =>
        {
            var mine = inPeriod.Where(s => s.HospitalId == h.Id).ToList();
            var all = rx.Where(s => s.HospitalId == h.Id).ToList();
            return new HospitalRow(h.Id, h.Name, h.Kind, mine.Count, mine.Select(s => s.PatientId).Distinct().Count(),
                mine.Sum(s => s.PatientShare + s.InstitutionShare), mine.Sum(s => s.InstitutionShare), total == 0 ? 0 : mine.Count / (double)total,
                all.Count(s => s.SoldAt >= last30), all.Count(s => s.SoldAt >= prev30 && s.SoldAt < last30),
                mine.GroupBy(s => s.Branch).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key);
        }).OrderByDescending(r => r.Prescriptions).ToList();
    }
}

public sealed record HospitalOption(int Id, string Name, HospitalKind Kind)
{
    public string KindText => Texts.Of(Kind);
}

public sealed record GetHospitalOptions : IQuery<IReadOnlyList<HospitalOption>>;

public sealed class GetHospitalOptionsHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetHospitalOptions, IReadOnlyList<HospitalOption>>
{
    public async Task<Result<IReadOnlyList<HospitalOption>>> HandleAsync(GetHospitalOptions q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var list = await uow.Repository<Hospital>().ListAsync(new Specification<Hospital>().OrderBy(h => h.Name), h => new HospitalOption(h.Id, h.Name, h.Kind), cancellationToken);
        return list.ToList();
    }
}

public sealed record SaveHospital(string Name, HospitalKind Kind) : ICommand<int>;

public sealed class SaveHospitalValidator : AbstractValidator<SaveHospital>
{
    public SaveHospitalValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(120).WithName("Kurum adı");
}

public sealed class SaveHospitalHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<SaveHospital, int>
{
    public async Task<Result<int>> HandleAsync(SaveHospital c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        if (await uow.Repository<Hospital>().AnyAsync(new Specification<Hospital>().Where(h => h.Name == c.Name.Trim()), cancellationToken))
            throw new DomainException($"{c.Name} zaten kayıtlı.");
        var h = new Hospital(c.Name, c.Kind);
        uow.Repository<Hospital>().Add(h);
        await uow.SaveChangesAsync(cancellationToken);
        return h.Id;
    }
}

public sealed record PatientOption(int Id, string FullName, int BirthYear, InsuranceType Insurance, bool ChronicReport)
{
    public string Display => $"{FullName} ({BirthYear})";
    public string InsuranceText => Texts.Of(Insurance);
}

public sealed record GetPatientOptions : IQuery<IReadOnlyList<PatientOption>>;

public sealed class GetPatientOptionsHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetPatientOptions, IReadOnlyList<PatientOption>>
{
    public async Task<Result<IReadOnlyList<PatientOption>>> HandleAsync(GetPatientOptions q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var list = await uow.Repository<Patient>().ListAsync(new Specification<Patient>().OrderBy(p => p.FullName),
            p => new PatientOption(p.Id, p.FullName, p.BirthYear, p.Insurance, p.ChronicReport), cancellationToken);
        return list.ToList();
    }
}
