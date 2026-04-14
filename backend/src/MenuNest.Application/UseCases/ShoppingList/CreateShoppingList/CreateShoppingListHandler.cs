using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;

public sealed class CreateShoppingListHandler
    : ICommandHandler<CreateShoppingListCommand, ShoppingListDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<CreateShoppingListCommand> _validator;

    public CreateShoppingListHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<CreateShoppingListCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<ShoppingListDto> Handle(
        CreateShoppingListCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = Domain.Entities.ShoppingList.Create(familyId, command.Name, user.Id);
        _db.ShoppingLists.Add(list);

        if (command.FromDate.HasValue && command.ToDate.HasValue)
        {
            await AutoGenerateItems(list, familyId, command.FromDate.Value, command.ToDate.Value, ct);
        }

        await _db.SaveChangesAsync(ct);

        var itemCount = list.Items.Count;
        return new ShoppingListDto(
            list.Id, list.Name, list.Status,
            itemCount, 0, list.CreatedAt, list.CompletedAt);
    }

    private async Task AutoGenerateItems(
        Domain.Entities.ShoppingList list,
        Guid familyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct)
    {
        // 1. Load Planned entries in range
        var entries = await _db.MealPlanEntries
            .Where(e => e.FamilyId == familyId
                && e.Status == MealEntryStatus.Planned
                && e.Date >= fromDate && e.Date <= toDate)
            .ToListAsync(ct);

        if (entries.Count == 0) return;

        // 2. Load recipes with ingredients
        var recipeIds = entries.Select(e => e.RecipeId).Distinct().ToList();
        var recipes = await _db.Recipes
            .Include(r => r.Ingredients)
            .Where(r => recipeIds.Contains(r.Id) && r.FamilyId == familyId)
            .ToListAsync(ct);

        // 3. Aggregate required per ingredient, tracking source entry ids
        var required = new Dictionary<Guid, decimal>();
        var sources = new Dictionary<Guid, List<Guid>>();
        foreach (var entry in entries)
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

        // 4. Load stock
        var ingredientIds = required.Keys.ToList();
        var stockLookup = await _db.StockItems
            .Where(s => s.FamilyId == familyId && ingredientIds.Contains(s.IngredientId))
            .ToDictionaryAsync(s => s.IngredientId, s => s.Quantity, ct);

        // 5. Create items for missing quantities
        foreach (var (ingredientId, totalRequired) in required)
        {
            var available = stockLookup.GetValueOrDefault(ingredientId);
            var missing = totalRequired - available;
            if (missing <= 0m) continue;

            list.AddOrIncreaseItem(ingredientId, missing, sources[ingredientId]);
        }
    }
}
