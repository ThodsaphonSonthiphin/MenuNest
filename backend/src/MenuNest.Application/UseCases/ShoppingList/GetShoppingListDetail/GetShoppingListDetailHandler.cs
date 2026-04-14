using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;

public sealed class GetShoppingListDetailHandler
    : IQueryHandler<GetShoppingListDetailQuery, ShoppingListDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetShoppingListDetailHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<ShoppingListDetailDto> Handle(
        GetShoppingListDetailQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == query.Id && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        var ingredientIds = list.Items.Select(i => i.IngredientId).Distinct().ToList();
        var ingredients = await _db.Ingredients
            .Where(i => ingredientIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var items = list.Items
            .Select(i =>
            {
                var meta = ingredients[i.IngredientId];
                return new ShoppingListItemDto(
                    i.Id, i.IngredientId, meta.Name, meta.Unit,
                    i.Quantity, i.IsBought, i.BoughtAt,
                    i.SourceMealPlanEntryIds.Count > 0 ? i.SourceMealPlanEntryIds : null);
            })
            .OrderBy(i => i.IsBought)
            .ThenBy(i => i.IngredientName)
            .ToList();

        return new ShoppingListDetailDto(
            list.Id, list.Name, list.Status,
            items.Count, items.Count(i => i.IsBought),
            list.CreatedAt, list.CompletedAt, items);
    }
}
