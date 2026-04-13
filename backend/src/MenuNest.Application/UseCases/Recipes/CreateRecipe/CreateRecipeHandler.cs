using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Recipes.CreateRecipe;

public sealed class CreateRecipeHandler : ICommandHandler<CreateRecipeCommand, RecipeDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<CreateRecipeCommand> _validator;

    public CreateRecipeHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<CreateRecipeCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<RecipeDetailDto> Handle(CreateRecipeCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var ingredientIds = command.Ingredients.Select(i => i.IngredientId).Distinct().ToList();
        var ingredients = await _db.Ingredients
            .Where(i => i.FamilyId == familyId && ingredientIds.Contains(i.Id))
            .ToListAsync(ct);

        if (ingredients.Count != ingredientIds.Count)
        {
            throw new DomainException("One or more ingredients were not found in this family's master list.");
        }

        var recipe = Recipe.Create(familyId, command.Name, user.Id, command.Description);
        foreach (var line in command.Ingredients)
        {
            recipe.AddIngredient(line.IngredientId, line.Quantity);
        }

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync(ct);

        return ToDetailDto(recipe, ingredients);
    }

    internal static RecipeDetailDto ToDetailDto(Recipe recipe, IReadOnlyList<Ingredient> ingredients)
    {
        var lookup = ingredients.ToDictionary(i => i.Id);
        var lines = recipe.Ingredients
            .Select(ri => new RecipeIngredientDto(
                ri.IngredientId,
                lookup[ri.IngredientId].Name,
                lookup[ri.IngredientId].Unit,
                ri.Quantity))
            .ToList();

        return new RecipeDetailDto(
            recipe.Id,
            recipe.Name,
            recipe.Description,
            recipe.ImageBlobPath,
            lines);
    }
}
