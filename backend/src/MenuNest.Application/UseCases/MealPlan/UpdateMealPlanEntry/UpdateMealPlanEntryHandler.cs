using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.MealPlan.UpdateMealPlanEntry;

public sealed class UpdateMealPlanEntryHandler : ICommandHandler<UpdateMealPlanEntryCommand, MealPlanEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public UpdateMealPlanEntryHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<MealPlanEntryDto> Handle(UpdateMealPlanEntryCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var entry = await _db.MealPlanEntries
            .FirstOrDefaultAsync(m => m.Id == command.Id && m.FamilyId == familyId, ct)
            ?? throw new DomainException("Meal plan entry not found.");

        var recipe = await _db.Recipes
            .FirstOrDefaultAsync(r => r.Id == command.RecipeId && r.FamilyId == familyId, ct)
            ?? throw new DomainException("Recipe not found.");

        if (entry.RecipeId != command.RecipeId)
        {
            entry.ChangeRecipe(command.RecipeId);
        }
        entry.UpdateNotes(command.Notes);

        await _db.SaveChangesAsync(ct);

        return new MealPlanEntryDto(
            entry.Id,
            entry.Date,
            entry.MealSlot,
            recipe.Id,
            recipe.Name,
            entry.Notes,
            entry.Status,
            entry.CookedAt,
            entry.CookNotes);
    }
}
