using Eczane.Domain;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace Eczane.Application;

public sealed record InvoiceRow(int? Id, Institution Institution, int Year, int Month, decimal Amount, int Prescriptions, DateOnly? IssuedOn, DateOnly? PaidOn)
{
    public string InstitutionText => Texts.Of(Institution);
    public string Period => Texts.MonthName(Year, Month);
    public string StatusText => Id is null ? "Faturalanmadı" : PaidOn is not null ? "Tahsil edildi" : "Tahsil bekliyor";
}

/// <summary>Kurum alacakları: kesilmiş faturalar ve henüz faturalanmamış aylar (o ayın reçetelerindeki kurum payı).</summary>
public sealed record ListInvoices : IQuery<IReadOnlyList<InvoiceRow>>;

public sealed class ListInvoicesHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<ListInvoices, IReadOnlyList<InvoiceRow>>
{
    public async Task<Result<IReadOnlyList<InvoiceRow>>> HandleAsync(ListInvoices q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var invoices = await uow.Repository<InstitutionInvoice>().ListAsync(cancellationToken: cancellationToken);
        var sales = await uow.Repository<Sale>().ListAsync(new Specification<Sale>().Where(s => s.Kind == SaleKind.Prescription && s.Status == SaleStatus.Completed && s.InstitutionShare > 0),
            s => new { s.SoldAt, s.Insurance, s.InstitutionShare }, cancellationToken);
        var rows = invoices.Select(i => new InvoiceRow(i.Id, i.Institution, i.Year, i.Month, i.Amount, i.Prescriptions, i.IssuedOn, i.PaidOn)).ToList();
        foreach (var g in sales.GroupBy(s => (Inst: s.Insurance == InsuranceType.Sgk ? Institution.Sgk : Institution.PrivateInsurance, s.SoldAt.Year, s.SoldAt.Month)))
        {
            if (invoices.Any(i => i.Institution == g.Key.Inst && i.Year == g.Key.Year && i.Month == g.Key.Month)) continue;
            rows.Add(new InvoiceRow(null, g.Key.Inst, g.Key.Year, g.Key.Month, g.Sum(s => s.InstitutionShare), g.Count(), null, null));
        }
        return rows.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenBy(r => r.Institution).ToList();
    }
}

/// <summary>Biten ayın kurum faturasını keser. İçinde bulunulan ay faturalanamaz.</summary>
public sealed record IssueInvoice(Institution Institution, int Year, int Month) : ICommand<int>;

public sealed class IssueInvoiceHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<IssueInvoice, int>
{
    public async Task<Result<int>> HandleAsync(IssueInvoice c, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var first = new DateOnly(c.Year, c.Month, 1);
        if (first.AddMonths(1) > today) throw new DomainException("Ay bitmeden fatura kesilemez.");
        await using var uow = uowFactory.Create();
        if (await uow.Repository<InstitutionInvoice>().AnyAsync(new Specification<InstitutionInvoice>()
                .Where(i => i.Institution == c.Institution && i.Year == c.Year && i.Month == c.Month), cancellationToken))
            throw new DomainException("Bu ayın faturası zaten kesilmiş.");
        var start = first.Start();
        var end = first.AddMonths(1).Start();
        var insurance = c.Institution == Institution.Sgk ? InsuranceType.Sgk : InsuranceType.Private;
        var shares = await uow.Repository<Sale>().ListAsync(new Specification<Sale>().Where(s => s.Kind == SaleKind.Prescription && s.Status == SaleStatus.Completed
            && s.Insurance == insurance && s.SoldAt >= start && s.SoldAt < end && s.InstitutionShare > 0), s => s.InstitutionShare, cancellationToken);
        if (shares.Count == 0) throw new DomainException("Bu ay için kurum payı olan reçete yok.");
        var inv = new InstitutionInvoice(c.Institution, c.Year, c.Month, shares.Sum(), shares.Count, today);
        uow.Repository<InstitutionInvoice>().Add(inv);
        await uow.SaveChangesAsync(cancellationToken);
        return inv.Id;
    }
}

public sealed record MarkInvoicePaid(int InvoiceId, DateOnly On) : ICommand;

public sealed class MarkInvoicePaidHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<MarkInvoicePaid>
{
    public async Task<Result> HandleAsync(MarkInvoicePaid c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var inv = await uow.Repository<InstitutionInvoice>().GetRequiredAsync(c.InvoiceId, cancellationToken);
        inv.MarkPaid(c.On);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
