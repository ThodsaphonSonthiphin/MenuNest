using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Ingredients.ListIngredients;

public sealed class ListIngredientsHandler : IQueryHandler<ListIngredientsQuery, IReadOnlyList<IngredientDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListIngredientsHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<IngredientDto>> Handle(
        ListIngredientsQuery query,
        CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var items = await _db.Ingredients
            .Where(i => i.FamilyId == familyId)
            .OrderBy(i => i.Name)
            .Select(i => new IngredientDto(i.Id, i.Name, i.Unit))
            .ToListAsync(ct);

        return items;
    }
}
