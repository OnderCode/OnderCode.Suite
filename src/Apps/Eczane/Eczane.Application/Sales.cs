using Eczane.Domain;
using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Queries;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace Eczane.Application;

// ---------- Satış / reçete ----------

public sealed record SaleItem(int ProductId, int Quantity);

public sealed record SaleDraft(
    SaleKind Kind, IReadOnlyList<SaleItem> Items, int? PatientId = null, int? HospitalId = null, string? Branch = null,
    PrescriptionColor Color = PrescriptionColor.White, string? PrescriptionNo = null, DateOnly? PrescribedOn = null,
    PaymentType Payment = PaymentType.Cash, decimal Discount = 0m, int? SoldById = null);

public sealed record ReceiptLine(string Product, string LotNo, DateOnly Expiry, int Quantity, decimal UnitPrice, decimal Total, bool Reimbursed);

public sealed record SaleReceipt(int SaleId, string Number, SaleKind Kind, decimal Total, decimal Discount, decimal PatientShare, decimal InstitutionShare,
    decimal Cost, IReadOnlyList<ReceiptLine> Lines, string? PatientName, InsuranceType Insurance)
{
    public decimal Profit => PatientShare + InstitutionShare - Cost;
    public string InsuranceText => Texts.Of(Insurance);
}

/// <summary>Satışı kaydetmeden payları ve parti dağıtımını (FEFO) hesaplar: ekranda toplamlar canlı görünsün.</summary>
public sealed record QuoteSale(SaleDraft Draft) : IQuery<SaleReceipt>;

/// <summary>Satışı kaydeder: stoktan SKT'si en yakın partiden düşer, hasta ve kurum payını hesaplar.</summary>
public sealed record CreateSale(SaleDraft Draft) : ICommand<SaleReceipt>;

public sealed class CreateSaleValidator : AbstractValidator<CreateSale>
{
    public CreateSaleValidator()
    {
        RuleFor(x => x.Draft.Items).NotEmpty().WithMessage("Satışa en az bir ürün ekleyin.");
        RuleForEach(x => x.Draft.Items).Must(i => i.Quantity is > 0 and <= 100).WithMessage("Kutu sayısı 1-100 arasında olmalı.");
        When(x => x.Draft.Kind == SaleKind.Prescription, () =>
        {
            RuleFor(x => x.Draft.PatientId).NotNull().WithMessage("Reçete için hasta seçin.");
            RuleFor(x => x.Draft.HospitalId).NotNull().WithMessage("Reçetenin yazıldığı kurumu seçin.");
            RuleFor(x => x.Draft.Branch).NotEmpty().WithMessage("Hekim branşını seçin.");
            RuleFor(x => x.Draft.PrescriptionNo).NotEmpty().MaximumLength(20).WithMessage("Reçete numarasını girin.");
        });
        RuleFor(x => x.Draft.Discount).GreaterThanOrEqualTo(0).WithName("İndirim");
    }
}

internal static class SaleBuilder
{
    /// <summary>Taslaktan satış kurar; partilerden FEFO ile düşer. Kaydetme çağırana kalır.</summary>
    public static async Task<Sale> BuildAsync(IUnitOfWork uow, SaleDraft d, string number, DateTime now, bool tracking, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        Employee? soldBy = null;
        if (d.SoldById is int eid) soldBy = await Load<Employee>(uow, eid, tracking, "Personel", ct);

        Sale sale;
        if (d.Kind == SaleKind.Prescription)
        {
            var patient = await Load<Patient>(uow, d.PatientId ?? 0, tracking, "Hasta", ct);
            var hospital = await Load<Hospital>(uow, d.HospitalId ?? 0, tracking, "Kurum", ct);
            sale = Sale.Prescription(number, now, patient, hospital, d.Branch ?? "", d.Color, d.PrescriptionNo ?? "", d.PrescribedOn ?? today, soldBy);
        }
        else
        {
            Patient? patient = d.PatientId is int pid ? await Load<Patient>(uow, pid, tracking, "Hasta", ct) : null;
            sale = Sale.Retail(number, now, soldBy, patient);
        }

        var ids = d.Items.Select(i => i.ProductId).Distinct().ToList();
        var productSpec = new Specification<Product>().Where(p => ids.Contains(p.Id));
        var lotSpec = new Specification<StockLot>().Where(l => ids.Contains(l.ProductId) && l.Quantity > 0 && l.Expiry > today).Include(l => l.Product);
        if (tracking) { productSpec.Tracking(); lotSpec.Tracking(); }
        var products = (await uow.Repository<Product>().ListAsync(productSpec, ct)).ToDictionary(p => p.Id);
        var lots = await uow.Repository<StockLot>().ListAsync(lotSpec, ct);
        foreach (var item in d.Items.GroupBy(i => i.ProductId).Select(g => new SaleItem(g.Key, g.Sum(i => i.Quantity))))
        {
            var product = products.GetValueOrDefault(item.ProductId) ?? throw new NotFoundException("Ürün", item.ProductId);
            var productLots = lots.Where(l => l.ProductId == product.Id).ToList();
            foreach (var (lot, qty) in StockRules.Allocate(productLots, item.Quantity, today))
                sale.AddLine(product, lot, qty, today);
        }
        sale.Complete(d.Payment, d.Discount);
        return sale;
    }

    static async Task<T> Load<T>(IUnitOfWork uow, int id, bool tracking, string name, CancellationToken ct) where T : OnderCode.Kernel.Domain.Entity
    {
        var spec = new Specification<T>().Where(x => x.Id == id);
        if (tracking) spec.Tracking();
        return await uow.Repository<T>().FirstOrDefaultAsync(spec, ct) ?? throw new NotFoundException(name, id);
    }

    public static SaleReceipt Receipt(Sale s) => new(s.Id, s.Number, s.Kind, s.Total, s.Discount, s.PatientShare, s.InstitutionShare, s.Cost,
        s.Lines.Select(l => new ReceiptLine(l.Product.Name, l.Lot.LotNo, l.Lot.Expiry, l.Quantity, l.UnitPrice, l.LineTotal, l.Reimbursed)).ToList(),
        s.Patient?.FullName, s.Insurance);

    public static async Task<string> NextNumberAsync(IUnitOfWork uow, DateTime now, CancellationToken ct)
    {
        var start = now.Date;
        var end = start.AddDays(1);
        var count = await uow.Repository<Sale>().CountAsync(new Specification<Sale>().Where(s => s.SoldAt >= start && s.SoldAt < end), ct);
        return $"{now:yyMMdd}-{count + 1:0000}";
    }
}

public sealed class QuoteSaleHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<QuoteSale, SaleReceipt>
{
    public async Task<Result<SaleReceipt>> HandleAsync(QuoteSale q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var sale = await SaleBuilder.BuildAsync(uow, q.Draft, "ÖNİZLEME", time.Now(), tracking: false, cancellationToken);
        return SaleBuilder.Receipt(sale);
    }
}

public sealed class CreateSaleHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<CreateSale, SaleReceipt>
{
    public async Task<Result<SaleReceipt>> HandleAsync(CreateSale c, CancellationToken cancellationToken)
    {
        var now = time.Now();
        await using var uow = uowFactory.Create();
        return await uow.ExecuteInTransactionAsync(async ct =>
        {
            var number = await SaleBuilder.NextNumberAsync(uow, now, ct);
            var sale = await SaleBuilder.BuildAsync(uow, c.Draft, number, now, tracking: true, ct);
            uow.Repository<Sale>().Add(sale);
            await uow.SaveChangesAsync(ct);
            return Result<SaleReceipt>.Success(SaleBuilder.Receipt(sale));
        }, cancellationToken);
    }
}

/// <summary>Satış iadesi: kutular aynı partilere geri döner, satış ciro dışına çıkar.</summary>
public sealed record ReturnSale(int SaleId) : ICommand;

public sealed class ReturnSaleHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<ReturnSale>
{
    public async Task<Result> HandleAsync(ReturnSale c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var sale = await uow.Repository<Sale>().FirstOrDefaultAsync(
            new Specification<Sale>().Where(s => s.Id == c.SaleId).Include(s => s.Lines).Include("Lines.Lot").Tracking(), cancellationToken)
                   ?? throw new NotFoundException("Satış", c.SaleId);
        if (time.Now() - sale.SoldAt > TimeSpan.FromDays(30)) throw new DomainException("30 günden eski satış iade alınamaz.");
        sale.Return(time.Now());
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---------- Satış listesi ----------

public enum SalePeriod { Today, Last7, Last30, All }

public sealed record SaleRow(int Id, string Number, DateTime SoldAt, SaleKind Kind, string? Patient, string? Hospital, string? Branch,
    PrescriptionColor? Color, string? PrescriptionNo, int Boxes, decimal Total, decimal PatientShare, decimal InstitutionShare, decimal Profit,
    PaymentType? Payment, SaleStatus Status, string? SoldBy, InsuranceType Insurance)
{
    public string KindText => Texts.Of(Kind);
    public string ColorText => Color is { } c ? Texts.Of(c) : "";
    public string PaymentText => Payment is { } p ? Texts.Of(p) : "";
    public string StatusText => Texts.Of(Status);
    public string InsuranceText => Kind == SaleKind.Prescription ? Texts.Of(Insurance) : "";
}

public sealed record ListSales : PagedQuery<SaleRow>
{
    public SalePeriod Period { get; init; } = SalePeriod.Today;
    public SaleKind? Kind { get; init; }
    public int? PatientId { get; init; }
}

public sealed class ListSalesValidator : PagedQueryValidator<ListSales, SaleRow>
{
}

public sealed class ListSalesHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListSales, PagedResult<SaleRow>>
{
    public async Task<Result<PagedResult<SaleRow>>> HandleAsync(ListSales q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var spec = new Specification<Sale>().Where(s => s.Status != SaleStatus.Open).OrderByDescending(s => s.SoldAt);
        DateTime? from = q.Period switch
        {
            SalePeriod.Today => today.Start(),
            SalePeriod.Last7 => today.AddDays(-6).Start(),
            SalePeriod.Last30 => today.AddDays(-29).Start(),
            _ => null,
        };
        if (from is { } f) spec.Where(s => s.SoldAt >= f);
        if (q.Kind is { } k) spec.Where(s => s.Kind == k);
        if (q.PatientId is { } pid) spec.Where(s => s.PatientId == pid);
        spec.Search(q.Search, s => s.Number, s => s.PrescriptionNo, s => s.Patient!.FullName, s => s.Hospital!.Name, s => s.Branch);
        var repo = uow.Repository<Sale>();
        var total = await repo.CountAsync(spec, cancellationToken);
        spec.Page((q.Page - 1) * q.PageSize, q.PageSize);
        var data = await repo.ListAsync(spec, s => new
        {
            s.Id, s.Number, s.SoldAt, s.Kind, Patient = s.Patient!.FullName, Hospital = s.Hospital!.Name, s.Branch, s.Color, s.PrescriptionNo,
            Boxes = s.Lines.Sum(l => l.Quantity), s.Total, s.PatientShare, s.InstitutionShare, s.Cost, s.Payment, s.Status, SoldBy = s.SoldBy!.FullName, s.Insurance,
        }, cancellationToken);
        var rows = data.Select(s => new SaleRow(s.Id, s.Number, s.SoldAt, s.Kind, s.Patient, s.Hospital, s.Branch, s.Color, s.PrescriptionNo, s.Boxes,
            s.Total, s.PatientShare, s.InstitutionShare, s.PatientShare + s.InstitutionShare - s.Cost, s.Payment, s.Status, s.SoldBy, s.Insurance)).ToList();
        return new PagedResult<SaleRow>(rows, total, q.Page, q.PageSize);
    }
}

public sealed record SaleDetail(SaleRow Sale, IReadOnlyList<ReceiptLine> Lines);

public sealed record GetSale(int SaleId) : IQuery<SaleDetail>;

public sealed class GetSaleHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetSale, SaleDetail>
{
    public async Task<Result<SaleDetail>> HandleAsync(GetSale q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var s = await uow.Repository<Sale>().FirstOrDefaultAsync(new Specification<Sale>().Where(x => x.Id == q.SaleId)
            .Include(x => x.Patient).Include(x => x.Hospital).Include(x => x.SoldBy).Include(x => x.Lines).Include("Lines.Product").Include("Lines.Lot"), cancellationToken)
            ?? throw new NotFoundException("Satış", q.SaleId);
        var row = new SaleRow(s.Id, s.Number, s.SoldAt, s.Kind, s.Patient?.FullName, s.Hospital?.Name, s.Branch, s.Color, s.PrescriptionNo, s.Boxes,
            s.Total, s.PatientShare, s.InstitutionShare, s.GrossProfit, s.Payment, s.Status, s.SoldBy?.FullName, s.Insurance);
        return new SaleDetail(row, SaleBuilder.Receipt(s).Lines);
    }
}
