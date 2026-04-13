using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Recipes.ListRecipes;

public sealed class ListRecipesHandler : IQueryHandler<ListRecipesQuery, IReadOnlyList<RecipeSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListRecipesHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<RecipeSummaryDto>> Handle(ListRecipesQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var items = await _db.Recipes
            .Where(r => r.FamilyId == familyId)
            .OrderBy(r => r.Name)
            .Select(r => new RecipeSummaryDto(
                r.Id,
                r.Name,
                r.Description,
                r.ImageBlobPath,
                r.Ingredients.Count))
            .ToListAsync(ct);

        return items;
    }
}
