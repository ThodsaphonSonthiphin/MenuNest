using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.RegenerateShoppingList;

public sealed class RegenerateShoppingListHandler
    : ICommandHandler<RegenerateShoppingListCommand, ShoppingListDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public RegenerateShoppingListHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<ShoppingListDetailDto> Handle(
        RegenerateShoppingListCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        // 1. Load list with items
        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == command.Id && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        // 2. Collect ALL source entry ids before removing anything
        var allSourceEntryIds = list.Items
            .SelectMany(i => i.SourceMealPlanEntryIds)
            .Distinct()
            .ToList();

        // 3. Remove all unbought items
        var unboughtItems = list.Items.Where(i => !i.IsBought).ToList();
        foreach (var item in unboughtItems)
        {
            list.RemoveItem(item.Id);
            _db.ShoppingListItems.Remove(item);
        }

        if (allSourceEntryIds.Count > 0)
        {
            // 4. Load source entries, filter to Planned only
            var plannedEntries = await _db.MealPlanEntries
                .Where(e => allSourceEntryIds.Contains(e.Id) && e.Status == MealEntryStatus.Planned)
                .ToListAsync(ct);

            if (plannedEntries.Count > 0)
            {
                // 5. Load recipes with ingredients
                var recipeIds = plannedEntries.Select(e => e.RecipeId).Distinct().ToList();
                var recipes = await _db.Recipes
                    .Include(r => r.Ingredients)
                    .Where(r => recipeIds.Contains(r.Id) && r.FamilyId == familyId)
                    .ToListAsync(ct);

                // 6. Aggregate required per ingredient
                var required = new Dictionary<Guid, decimal>();
                var sources = new Dictionary<Guid, List<Guid>>();
                foreach (var entry in plannedEntries)
                {
                    var recipe = recipes.SingleOrDefault(r => r.Id == entry.RecipeId);
                    if (recipe is null) continue;
                    foreach (var ri in recipe.Ingredients)
                    {
                        required[ri.IngredientId] = required.GetValueOrDefault(ri.IngredientId) + ri.Quantity;
                        if (!sources.ContainsKey(ri.IngredientId))
                            sources[ri.IngredientId] = new List<Guid>();
                        if (!sources[ri.IngredientId].Contains(entry.Id))
                            sources[ri.IngredientId].Add(entry.Id);
                    }
                }

                // 7. Load stock
                var ingredientIds = required.Keys.ToList();
                var stockLookup = await _db.StockItems
                    .Where(s => s.FamilyId == familyId && ingredientIds.Contains(s.IngredientId))
                    .ToDictionaryAsync(s => s.IngredientId, s => s.Quantity, ct);

                // 8. For each missing ingredient, skip if a bought item already exists
                var boughtIngredientIds = list.Items
                    .Where(i => i.IsBought)
                    .Select(i => i.IngredientId)
                    .ToHashSet();

                foreach (var (ingredientId, totalRequired) in required)
                {
                    var available = stockLookup.GetValueOrDefault(ingredientId);
                    var missing = totalRequired - available;
                    if (missing <= 0m) continue;

                    // Don't add if a bought item already covers this ingredient
                    if (boughtIngredientIds.Contains(ingredientId)) continue;

                    var newItem = list.AddOrIncreaseItem(ingredientId, missing, sources[ingredientId]);
                    _db.ShoppingListItems.Add(newItem);
                }
            }
        }

        // 9. Save
        await _db.SaveChangesAsync(ct);

        // 10. Return detail DTO (re-query for ingredient names)
        var allIngredientIds = list.Items.Select(i => i.IngredientId).Distinct().ToList();
        var ingredients = await _db.Ingredients
            .Where(i => allIngredientIds.Contains(i.Id))
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
