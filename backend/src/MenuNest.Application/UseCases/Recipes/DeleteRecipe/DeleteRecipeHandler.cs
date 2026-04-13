using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Recipes.DeleteRecipe;

public sealed class DeleteRecipeHandler : ICommandHandler<DeleteRecipeCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteRecipeHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteRecipeCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var recipe = await _db.Recipes
            .FirstOrDefaultAsync(r => r.Id == command.Id && r.FamilyId == familyId, ct)
            ?? throw new DomainException("Recipe not found.");

        var usedInMealPlan = await _db.MealPlanEntries.AnyAsync(m => m.RecipeId == recipe.Id, ct);
        if (usedInMealPlan)
        {
            throw new DomainException(
                $"Cannot delete '{recipe.Name}' — it is scheduled on the meal plan. Remove the meal plan entries first.");
        }

        _db.Recipes.Remove(recipe);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
