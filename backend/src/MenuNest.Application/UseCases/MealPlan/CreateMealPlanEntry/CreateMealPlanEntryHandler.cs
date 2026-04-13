using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.MealPlan.CreateMealPlanEntry;

public sealed class CreateMealPlanEntryHandler : ICommandHandler<CreateMealPlanEntryCommand, MealPlanEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<CreateMealPlanEntryCommand> _validator;

    public CreateMealPlanEntryHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<CreateMealPlanEntryCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<MealPlanEntryDto> Handle(CreateMealPlanEntryCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var recipe = await _db.Recipes
            .FirstOrDefaultAsync(r => r.Id == command.RecipeId && r.FamilyId == familyId, ct)
            ?? throw new DomainException("Recipe not found.");

        var entry = MealPlanEntry.Create(
            familyId,
            command.Date,
            command.MealSlot,
            recipe.Id,
            user.Id,
            command.Notes);

        _db.MealPlanEntries.Add(entry);
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
