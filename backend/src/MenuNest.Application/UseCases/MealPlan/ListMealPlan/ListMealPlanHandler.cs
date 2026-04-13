using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.MealPlan.ListMealPlan;

public sealed class ListMealPlanHandler : IQueryHandler<ListMealPlanQuery, IReadOnlyList<MealPlanEntryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListMealPlanHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<MealPlanEntryDto>> Handle(ListMealPlanQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var from = query.From;
        var to = query.To;
        if (from > to)
        {
            (from, to) = (to, from);
        }

        // OrderBy must run on entity properties, not on the projected
        // positional record — see the matching note in ListStockHandler
        // for the EF-translation rationale.
        var items = await _db.MealPlanEntries
            .Where(m => m.FamilyId == familyId && m.Date >= from && m.Date <= to)
            .Join(
                _db.Recipes,
                m => m.RecipeId,
                r => r.Id,
                (m, r) => new { Entry = m, Recipe = r })
            .OrderBy(x => x.Entry.Date).ThenBy(x => x.Entry.MealSlot)
            .Select(x => new MealPlanEntryDto(
                x.Entry.Id,
                x.Entry.Date,
                x.Entry.MealSlot,
                x.Recipe.Id,
                x.Recipe.Name,
                x.Entry.Notes,
                x.Entry.Status,
                x.Entry.CookedAt,
                x.Entry.CookNotes))
            .ToListAsync(ct);

        return items;
    }
}
