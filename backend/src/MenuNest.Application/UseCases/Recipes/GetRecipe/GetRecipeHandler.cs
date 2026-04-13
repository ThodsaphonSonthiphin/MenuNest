using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Recipes.GetRecipe;

public sealed class GetRecipeHandler : IQueryHandler<GetRecipeQuery, RecipeDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetRecipeHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<RecipeDetailDto> Handle(GetRecipeQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        // Left-join against Ingredients so we can surface the name +
        // unit alongside the RecipeIngredient row without a second
        // round-trip from the client.
        var recipe = await _db.Recipes
            .Where(r => r.Id == query.Id && r.FamilyId == familyId)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.ImageBlobPath,
                Ingredients = r.Ingredients
                    .Join(
                        _db.Ingredients,
                        ri => ri.IngredientId,
                        i => i.Id,
                        (ri, i) => new RecipeIngredientDto(
                            i.Id,
                            i.Name,
                            i.Unit,
                            ri.Quantity))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Recipe not found.");

        return new RecipeDetailDto(
            recipe.Id,
            recipe.Name,
            recipe.Description,
            recipe.ImageBlobPath,
            recipe.Ingredients);
    }
}
