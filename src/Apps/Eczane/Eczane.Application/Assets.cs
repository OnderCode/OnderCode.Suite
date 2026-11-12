using Eczane.Domain;
using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace Eczane.Application;

public sealed record AssetRow(int Id, string Code, string Name, AssetCategory Category, string Location, DateOnly PurchaseDate, decimal Cost,
    int UsefulLifeYears, decimal Accumulated, decimal BookValue, decimal ThisYear, AssetStatus Status, DateOnly? NextMaintenance, int MaintenanceDays,
    decimal MaintenanceCost, string? SerialNo)
{
    public string CategoryText => Texts.Of(Category);
    public string StatusText => Texts.Of(Status);
    public double DepreciatedShare => Cost == 0 ? 0 : (double)(Accumulated / Cost);
    public string MaintenanceText => NextMaintenance is null ? "" : MaintenanceDays < 0 ? $"{-MaintenanceDays} gün gecikti" : MaintenanceDays <= 30 ? $"{MaintenanceDays} gün kaldı" : NextMaintenance.Value.ToString("dd.MM.yyyy");
}

public sealed record ListAssets(bool IncludeRetired = false, string? Search = null) : IQuery<IReadOnlyList<AssetRow>>;

public sealed class ListAssetsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListAssets, IReadOnlyList<AssetRow>>
{
    public async Task<Result<IReadOnlyList<AssetRow>>> HandleAsync(ListAssets q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var spec = new Specification<Asset>().OrderBy(a => a.Code).Search(q.Search, a => a.Name, a => a.Code, a => a.Location);
        if (!q.IncludeRetired) spec.Where(a => a.Status != AssetStatus.Retired);
        var assets = await uow.Repository<Asset>().ListAsync(spec, cancellationToken);
        var maint = await uow.Repository<MaintenanceRecord>().ListAsync(new Specification<MaintenanceRecord>(), m => new { m.AssetId, m.Cost }, cancellationToken);
        return assets.Select(a => new AssetRow(a.Id, a.Code, a.Name, a.Category, a.Location, a.PurchaseDate, a.Cost, a.UsefulLifeYears,
            a.AccumulatedDepreciation(today), a.BookValue(today), a.DepreciationInYear(today.Year), a.Status, a.NextMaintenance,
            a.NextMaintenance is { } n ? n.DayNumber - today.DayNumber : 0, maint.Where(m => m.AssetId == a.Id).Sum(m => m.Cost), a.SerialNo)).ToList();
    }
}

public sealed record SaveAsset(int? Id, string Code, string Name, AssetCategory Category, DateOnly PurchaseDate, decimal Cost, int UsefulLifeYears,
    string Location, decimal SalvageValue, int MaintenanceIntervalMonths, string? SerialNo) : ICommand<int>;

public sealed class SaveAssetValidator : AbstractValidator<SaveAsset>
{
    public SaveAssetValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).WithName("Kod");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120).WithName("Ad");
        RuleFor(x => x.Location).NotEmpty().MaximumLength(60).WithName("Konum");
        RuleFor(x => x.Cost).GreaterThan(0).WithName("Maliyet");
        RuleFor(x => x.UsefulLifeYears).InclusiveBetween(1, 50).WithName("Faydalı ömür");
        RuleFor(x => x.SalvageValue).GreaterThanOrEqualTo(0).LessThan(x => x.Cost).WithName("Hurda değeri");
        RuleFor(x => x.MaintenanceIntervalMonths).InclusiveBetween(0, 60).WithName("Bakım periyodu");
    }
}

public sealed class SaveAssetHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<SaveAsset, int>
{
    public async Task<Result<int>> HandleAsync(SaveAsset c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var code = c.Code.Trim().ToUpperInvariant();
        if (await uow.Repository<Asset>().AnyAsync(new Specification<Asset>().Where(a => a.Code == code && a.Id != (c.Id ?? 0)), cancellationToken))
            throw new DomainException($"{code} kodlu demirbaş zaten var.");
        Asset asset;
        if (c.Id is int id)
        {
            asset = await uow.Repository<Asset>().GetRequiredAsync(id, cancellationToken);
            asset.Move(c.Location);
        }
        else
        {
            asset = new Asset(code, c.Name, c.Category, c.PurchaseDate, c.Cost, c.UsefulLifeYears, c.Location, c.SalvageValue, c.MaintenanceIntervalMonths, c.SerialNo);
            uow.Repository<Asset>().Add(asset);
        }
        await uow.SaveChangesAsync(cancellationToken);
        return asset.Id;
    }
}

public sealed record AddMaintenance(int AssetId, DateOnly Date, MaintenanceKind Kind, decimal Cost, string? Note) : ICommand<int>;

public sealed class AddMaintenanceHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<AddMaintenance, int>
{
    public async Task<Result<int>> HandleAsync(AddMaintenance c, CancellationToken cancellationToken)
    {
        if (c.Date > time.Today()) throw new DomainException("İleri tarihli bakım girilemez.");
        await using var uow = uowFactory.Create();
        var asset = await uow.Repository<Asset>().GetRequiredAsync(c.AssetId, cancellationToken);
        var rec = new MaintenanceRecord(asset, c.Date, c.Kind, c.Cost, c.Note);
        uow.Repository<MaintenanceRecord>().Add(rec);
        await uow.SaveChangesAsync(cancellationToken);
        return rec.Id;
    }
}

public enum AssetAction { Repair, BackInService, Retire }

public sealed record ChangeAssetStatus(int AssetId, AssetAction Action) : ICommand;

public sealed class ChangeAssetStatusHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<ChangeAssetStatus>
{
    public async Task<Result> HandleAsync(ChangeAssetStatus c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var asset = await uow.Repository<Asset>().GetRequiredAsync(c.AssetId, cancellationToken);
        switch (c.Action)
        {
            case AssetAction.Repair: asset.SendToRepair(); break;
            case AssetAction.BackInService: asset.BackInService(); break;
            default: asset.Retire(time.Today()); break;
        }
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record MaintenanceRow(int Id, string Asset, DateOnly Date, MaintenanceKind Kind, decimal Cost, string? Note)
{
    public string KindText => Texts.Of(Kind);
}

public sealed record ListMaintenance(int? AssetId = null) : IQuery<IReadOnlyList<MaintenanceRow>>;

public sealed class ListMaintenanceHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<ListMaintenance, IReadOnlyList<MaintenanceRow>>
{
    public async Task<Result<IReadOnlyList<MaintenanceRow>>> HandleAsync(ListMaintenance q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var spec = new Specification<MaintenanceRecord>().OrderByDescending(m => m.Date);
        if (q.AssetId is int id) spec.Where(m => m.AssetId == id);
        var list = await uow.Repository<MaintenanceRecord>().ListAsync(spec, m => new MaintenanceRow(m.Id, m.Asset.Code + " · " + m.Asset.Name, m.Date, m.Kind, m.Cost, m.Note), cancellationToken);
        return list.ToList();
    }
}
