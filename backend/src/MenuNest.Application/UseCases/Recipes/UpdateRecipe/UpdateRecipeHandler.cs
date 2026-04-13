using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Recipes.CreateRecipe;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Recipes.UpdateRecipe;

public sealed class UpdateRecipeHandler : ICommandHandler<UpdateRecipeCommand, RecipeDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<UpdateRecipeCommand> _validator;

    public UpdateRecipeHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<UpdateRecipeCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<RecipeDetailDto> Handle(UpdateRecipeCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var recipe = await _db.Recipes
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == command.Id && r.FamilyId == familyId, ct)
            ?? throw new DomainException("Recipe not found.");

        var ingredientIds = command.Ingredients.Select(i => i.IngredientId).Distinct().ToList();
        var ingredients = await _db.Ingredients
            .Where(i => i.FamilyId == familyId && ingredientIds.Contains(i.Id))
            .ToListAsync(ct);
        if (ingredients.Count != ingredientIds.Count)
        {
            throw new DomainException("One or more ingredients were not found in this family's master list.");
        }

        recipe.UpdateDetails(command.Name, command.Description);

        // Reconcile the ingredient list: remove lines that dropped out,
        // add new ones, and update quantities on surviving lines.
        var incoming = command.Ingredients.ToDictionary(i => i.IngredientId, i => i.Quantity);
        var existingIds = recipe.Ingredients.Select(ri => ri.IngredientId).ToList();
        foreach (var existingId in existingIds)
        {
            if (!incoming.ContainsKey(existingId))
            {
                recipe.RemoveIngredient(existingId);
            }
        }
        foreach (var (ingredientId, quantity) in incoming)
        {
            if (existingIds.Contains(ingredientId))
            {
                recipe.UpdateIngredientQuantity(ingredientId, quantity);
            }
            else
            {
                recipe.AddIngredient(ingredientId, quantity);
            }
        }

        await _db.SaveChangesAsync(ct);

        return CreateRecipeHandler.ToDetailDto(recipe, ingredients);
    }
}
