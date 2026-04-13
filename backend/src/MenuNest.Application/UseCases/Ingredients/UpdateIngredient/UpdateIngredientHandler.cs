using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Ingredients.UpdateIngredient;

public sealed class UpdateIngredientHandler : ICommandHandler<UpdateIngredientCommand, IngredientDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<UpdateIngredientCommand> _validator;

    public UpdateIngredientHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<UpdateIngredientCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<IngredientDto> Handle(UpdateIngredientCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var ingredient = await _db.Ingredients
            .FirstOrDefaultAsync(i => i.Id == command.Id && i.FamilyId == familyId, ct)
            ?? throw new DomainException("Ingredient not found.");

        var trimmedName = command.Name.Trim();
        if (!string.Equals(ingredient.Name, trimmedName, StringComparison.Ordinal))
        {
            var nameTaken = await _db.Ingredients.AnyAsync(
                i => i.FamilyId == familyId && i.Id != command.Id && i.Name == trimmedName, ct);
            if (nameTaken)
            {
                throw new DomainException($"An ingredient named '{trimmedName}' already exists.");
            }
            ingredient.Rename(trimmedName);
        }

        ingredient.ChangeUnit(command.Unit);
        await _db.SaveChangesAsync(ct);

        return new IngredientDto(ingredient.Id, ingredient.Name, ingredient.Unit);
    }
}
