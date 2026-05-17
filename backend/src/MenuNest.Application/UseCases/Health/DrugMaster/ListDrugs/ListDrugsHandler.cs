using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.DrugMaster.ListDrugs;

public sealed class ListDrugsHandler
    : IQueryHandler<ListDrugsQuery, IReadOnlyList<DrugDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListDrugsHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<DrugDto>> Handle(
        ListDrugsQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        // Active drugs only (soft-deleted excluded).
        var drugs = await _db.Drugs
            .Where(d => d.UserId == user.Id && d.DeletedAt == null)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        // Optional symptom filter — applied client-side because TreatsSymptomIds
        // is a JSON column; the in-memory size is small enough that a server-side
        // contains query is not worth the complexity for Phase 1.
        if (query.SymptomId.HasValue)
        {
            drugs = drugs.Where(d => d.TreatsSymptomIds.Contains(query.SymptomId.Value)).ToList();
        }

        var drugIds = drugs.Select(d => d.Id).ToList();
        var firstPhotos = await _db.Photos
            .Where(p => p.ParentType == PhotoParentType.Drug
                && drugIds.Contains(p.ParentId)
                && p.DeletedAt == null)
            .GroupBy(p => p.ParentId)
            .Select(g => g.OrderBy(p => p.CreatedAt).First())
            .ToListAsync(ct);
        var firstPhotoByDrug = firstPhotos.ToDictionary(p => p.ParentId, p => p.BlobUrl);

        return drugs
            .Select(d => new DrugDto(
                d.Id, d.Name, d.ActiveIngredient, d.DrugType, d.DoseStrength,
                d.EffectDurationMinHours, d.EffectDurationMaxHours,
                d.MaxDailyDose, d.StockCount, d.ExpirationDate,
                d.TreatsSymptomIds,
                HasPhoto: firstPhotoByDrug.ContainsKey(d.Id),
                FirstPhotoUrl: firstPhotoByDrug.GetValueOrDefault(d.Id)))
            .ToList();
    }
}
