using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Stock.ListStock;

public sealed class ListStockHandler : IQueryHandler<ListStockQuery, IReadOnlyList<StockItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListStockHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<StockItemDto>> Handle(ListStockQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var items = await _db.StockItems
            .Where(s => s.FamilyId == familyId)
            .Join(
                _db.Ingredients,
                s => s.IngredientId,
                i => i.Id,
                (s, i) => new StockItemDto(
                    s.Id,
                    i.Id,
                    i.Name,
                    i.Unit,
                    s.Quantity,
                    s.UpdatedAt ?? s.CreatedAt,
                    s.UpdatedByUserId))
            .OrderBy(s => s.IngredientName)
            .ToListAsync(ct);

        return items;
    }
}
