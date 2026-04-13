using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Ingredients.DeleteIngredient;

public sealed class DeleteIngredientHandler : ICommandHandler<DeleteIngredientCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteIngredientHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteIngredientCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var ingredient = await _db.Ingredients
            .FirstOrDefaultAsync(i => i.Id == command.Id && i.FamilyId == familyId, ct)
            ?? throw new DomainException("Ingredient not found.");

        // Guard: refuse delete if the ingredient is referenced
        // elsewhere. The UI explains to the user where the references
        // live so they can clean those up first.
        var recipeCount = await _db.RecipeIngredients.CountAsync(r => r.IngredientId == ingredient.Id, ct);
        var stockCount = await _db.StockItems.CountAsync(s => s.IngredientId == ingredient.Id, ct);
        if (recipeCount > 0 || stockCount > 0)
        {
            throw new DomainException(
                $"Cannot delete '{ingredient.Name}' — it is used by {recipeCount} recipe(s) and {stockCount} stock entry/entries. Remove those references first.");
        }

        _db.Ingredients.Remove(ingredient);
        await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
