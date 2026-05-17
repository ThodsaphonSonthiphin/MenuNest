using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.DrugMaster.GetDrug;

public sealed class GetDrugHandler : IQueryHandler<GetDrugQuery, DrugDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetDrugHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<DrugDetailDto> Handle(GetDrugQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var drug = await _db.Drugs
            .FirstOrDefaultAsync(d =>
                d.Id == query.Id && d.UserId == user.Id && d.DeletedAt == null, ct)
            ?? throw new DomainException("Drug not found.");

        var photos = await _db.Photos
            .Where(p => p.ParentType == PhotoParentType.Drug
                && p.ParentId == drug.Id
                && p.DeletedAt == null)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PhotoRefDto(p.Id, p.BlobUrl, p.FileSize, p.ContentType))
            .ToListAsync(ct);

        return new DrugDetailDto(
            drug.Id, drug.Name, drug.ActiveIngredient, drug.DrugType, drug.DoseStrength,
            drug.EffectDurationMinHours, drug.EffectDurationMaxHours,
            drug.MaxDailyDose, drug.StockCount, drug.ExpirationDate, drug.UsageNote,
            drug.TreatsSymptomIds, photos,
            drug.CreatedAt, drug.UpdatedAt);
    }
}
