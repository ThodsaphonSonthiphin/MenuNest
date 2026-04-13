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

        // Note: Order BEFORE the DTO projection. EF Core can't translate
        // `OrderBy(s => s.IngredientName)` when `s` is already a positional
        // record produced by Join's result selector — it can't decompose the
        // constructor call back into a column reference. Sorting on the
        // entity property keeps the chain translatable.
        var items = await _db.StockItems
            .Where(s => s.FamilyId == familyId)
            .Join(
                _db.Ingredients,
                s => s.IngredientId,
                i => i.Id,
                (s, i) => new { Stock = s, Ingredient = i })
            .OrderBy(x => x.Ingredient.Name)
            .Select(x => new StockItemDto(
                x.Stock.Id,
                x.Ingredient.Id,
                x.Ingredient.Name,
                x.Ingredient.Unit,
                x.Stock.Quantity,
                x.Stock.UpdatedAt ?? x.Stock.CreatedAt,
                x.Stock.UpdatedByUserId))
            .ToListAsync(ct);

        return items;
    }
}
