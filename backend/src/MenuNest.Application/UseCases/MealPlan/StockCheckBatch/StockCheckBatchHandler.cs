using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.MealPlan;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.MealPlan.StockCheckBatch;

public sealed class StockCheckBatchHandler : IQueryHandler<StockCheckBatchQuery, StockCheckBatchDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<StockCheckBatchQuery> _validator;

    public StockCheckBatchHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<StockCheckBatchQuery> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<StockCheckBatchDto> Handle(StockCheckBatchQuery query, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(query, ct);

        if (query.EntryIds.Count == 0)
        {
            return new StockCheckBatchDto(Array.Empty<StockCheckLineDto>(), IsSufficient: true);
        }

        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);
        var ids = query.EntryIds.Distinct().ToArray();

        var entries = await _db.MealPlanEntries
            .Where(m => ids.Contains(m.Id) && m.FamilyId == familyId)
            .ToListAsync(ct);
        if (entries.Count != ids.Length)
        {
            throw new DomainException("One or more meal plan entries were not found.");
        }

        var recipeIds = entries.Select(e => e.RecipeId).Distinct().ToList();
        var recipes = await _db.Recipes
            .Include(r => r.Ingredients)
            .Where(r => recipeIds.Contains(r.Id) && r.FamilyId == familyId)
            .ToListAsync(ct);

        // Aggregate required quantity per ingredient across all entries.
        // Each entry contributes its recipe's full ingredient list — so if
        // the user planned ข้าวสวย twice, rice is summed twice.
        var required = new Dictionary<Guid, decimal>();
        foreach (var entry in entries)
        {
            var recipe = recipes.SingleOrDefault(r => r.Id == entry.RecipeId)
                ?? throw new DomainException("Recipe not found.");
            foreach (var ri in recipe.Ingredients)
            {
                required[ri.IngredientId] = required.GetValueOrDefault(ri.IngredientId) + ri.Quantity;
            }
        }

        var ingredientIds = required.Keys.ToList();

        var ingredients = await _db.Ingredients
            .Where(i => i.FamilyId == familyId && ingredientIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var stockLookup = await _db.StockItems
            .Where(s => s.FamilyId == familyId && ingredientIds.Contains(s.IngredientId))
            .ToDictionaryAsync(s => s.IngredientId, s => s.Quantity, ct);

        var lines = required
            .Select(kv =>
            {
                var meta = ingredients[kv.Key];
                var available = stockLookup.GetValueOrDefault(kv.Key);
                var missing = kv.Value > available ? kv.Value - available : 0m;
                return new StockCheckLineDto(meta.Id, meta.Name, meta.Unit, kv.Value, available, missing);
            })
            .OrderBy(l => l.IngredientName)
            .ToList();

        return new StockCheckBatchDto(lines, IsSufficient: lines.All(l => l.Missing == 0m));
    }
}
