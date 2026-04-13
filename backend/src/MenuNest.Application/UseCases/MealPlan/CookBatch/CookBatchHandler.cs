using System.Globalization;
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.MealPlan.CookBatch;

public sealed class CookBatchHandler : ICommandHandler<CookBatchCommand, CookBatchResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<CookBatchCommand> _validator;

    public CookBatchHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<CookBatchCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<CookBatchResult> Handle(CookBatchCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var ids = command.EntryIds.Distinct().ToArray();

        var entries = await _db.MealPlanEntries
            .Where(m => ids.Contains(m.Id) && m.FamilyId == familyId)
            .ToListAsync(ct);
        if (entries.Count != ids.Length)
        {
            throw new DomainException("One or more meal plan entries were not found.");
        }
        if (entries.Any(e => e.Status != MealEntryStatus.Planned))
        {
            throw new DomainException("Only planned entries can be cooked. Refresh and try again.");
        }

        var recipeIds = entries.Select(e => e.RecipeId).Distinct().ToList();
        var recipes = await _db.Recipes
            .Include(r => r.Ingredients)
            .Where(r => recipeIds.Contains(r.Id) && r.FamilyId == familyId)
            .ToListAsync(ct);

        // Aggregate required per ingredient — once per entry occurrence.
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
        var ingredientLookup = await _db.Ingredients
            .Where(i => i.FamilyId == familyId && ingredientIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var stockItems = await _db.StockItems
            .Where(s => s.FamilyId == familyId && ingredientIds.Contains(s.IngredientId))
            .ToListAsync(ct);
        var stockLookup = stockItems.ToDictionary(s => s.IngredientId);

        var deducted = new List<CookDeducted>();
        var partial = new List<CookShortfall>();
        var notesParts = new List<string>();

        // A batch cook has no single canonical source row; use the first
        // entry's id as a stable reference so the StockTransaction audit
        // trail still ties back to a meal plan entry.
        var sourceRefId = entries[0].Id;

        foreach (var (ingredientId, neededRaw) in required)
        {
            var meta = ingredientLookup.GetValueOrDefault(ingredientId)
                ?? throw new DomainException($"Ingredient {ingredientId} not found — data may be inconsistent.");
            var stock = stockLookup.GetValueOrDefault(ingredientId);

            // ApplyDelta returns the delta actually applied (clamped at 0).
            // For a totally missing stock row, applied = 0 and we still
            // record the shortfall in CookNotes / partial[] so the user
            // knows what to add to a shopping list.
            var applied = stock?.ApplyDelta(-neededRaw, user.Id) ?? 0m;
            var actuallyDeducted = -applied;

            if (actuallyDeducted > 0m)
            {
                deducted.Add(new CookDeducted(meta.Id, meta.Name, meta.Unit, actuallyDeducted));
                _db.StockTransactions.Add(StockTransaction.Create(
                    familyId,
                    meta.Id,
                    -actuallyDeducted,
                    StockTransactionSource.Cook,
                    sourceRefId: sourceRefId,
                    userId: user.Id,
                    notes: $"Batch cook of {entries.Count} entries"));
            }

            var missing = neededRaw - actuallyDeducted;
            if (missing > 0m)
            {
                partial.Add(new CookShortfall(meta.Id, meta.Name, meta.Unit, neededRaw, actuallyDeducted, missing));
                notesParts.Add($"ขาด {meta.Name} {missing.ToString(CultureInfo.InvariantCulture)} {meta.Unit}");
            }
        }

        var cookNotes = notesParts.Count == 0
            ? null
            : string.Join("; ", notesParts) + " — ใช้เท่าที่มี";

        foreach (var entry in entries)
        {
            entry.MarkCooked(user.Id, cookNotes);
        }

        await _db.SaveChangesAsync(ct);

        return new CookBatchResult(deducted, partial, entries.Select(e => e.Id).ToList());
    }
}
