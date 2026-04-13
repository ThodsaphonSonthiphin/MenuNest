using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.MealPlan.StockCheck;

/// <summary>
/// For a given meal plan entry, compares the recipe's ingredient
/// requirements against the family's current stock and reports per-
/// ingredient shortfalls. Consumed by the Meal Plan sidebar, and
/// later by the cook action's "will I have enough?" check.
/// </summary>
public sealed class StockCheckHandler : IQueryHandler<StockCheckQuery, StockCheckDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public StockCheckHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<StockCheckDto> Handle(StockCheckQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var entry = await _db.MealPlanEntries
            .FirstOrDefaultAsync(m => m.Id == query.MealPlanEntryId && m.FamilyId == familyId, ct)
            ?? throw new DomainException("Meal plan entry not found.");

        var recipe = await _db.Recipes
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == entry.RecipeId && r.FamilyId == familyId, ct)
            ?? throw new DomainException("Recipe not found.");

        var ingredientIds = recipe.Ingredients.Select(ri => ri.IngredientId).ToList();

        var ingredients = await _db.Ingredients
            .Where(i => i.FamilyId == familyId && ingredientIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var stockLookup = await _db.StockItems
            .Where(s => s.FamilyId == familyId && ingredientIds.Contains(s.IngredientId))
            .ToDictionaryAsync(s => s.IngredientId, s => s.Quantity, ct);

        var lines = recipe.Ingredients
            .Select(ri =>
            {
                var meta = ingredients[ri.IngredientId];
                var available = stockLookup.GetValueOrDefault(ri.IngredientId);
                var missing = ri.Quantity > available ? ri.Quantity - available : 0m;
                return new StockCheckLineDto(
                    meta.Id,
                    meta.Name,
                    meta.Unit,
                    ri.Quantity,
                    available,
                    missing);
            })
            .OrderBy(l => l.IngredientName)
            .ToList();

        return new StockCheckDto(
            entry.Id,
            recipe.Id,
            recipe.Name,
            lines,
            IsSufficient: lines.All(l => l.Missing == 0m));
    }
}
