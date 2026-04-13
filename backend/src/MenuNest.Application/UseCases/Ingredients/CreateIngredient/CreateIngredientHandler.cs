using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Ingredients.CreateIngredient;

public sealed class CreateIngredientHandler : ICommandHandler<CreateIngredientCommand, IngredientDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<CreateIngredientCommand> _validator;

    public CreateIngredientHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<CreateIngredientCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<IngredientDto> Handle(CreateIngredientCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var trimmedName = command.Name.Trim();
        var exists = await _db.Ingredients
            .AnyAsync(i => i.FamilyId == familyId && i.Name == trimmedName, ct);
        if (exists)
        {
            throw new DomainException($"An ingredient named '{trimmedName}' already exists.");
        }

        var ingredient = Ingredient.Create(familyId, trimmedName, command.Unit);
        _db.Ingredients.Add(ingredient);
        await _db.SaveChangesAsync(ct);

        return new IngredientDto(ingredient.Id, ingredient.Name, ingredient.Unit);
    }
}
