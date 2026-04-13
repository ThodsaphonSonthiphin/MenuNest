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

        var items = await _db.MealPlanEntries
            .Where(m => m.FamilyId == familyId && m.Date >= from && m.Date <= to)
            .Join(
                _db.Recipes,
                m => m.RecipeId,
                r => r.Id,
                (m, r) => new MealPlanEntryDto(
                    m.Id,
                    m.Date,
                    m.MealSlot,
                    r.Id,
                    r.Name,
                    m.Notes,
                    m.Status,
                    m.CookedAt,
                    m.CookNotes))
            .OrderBy(e => e.Date).ThenBy(e => e.MealSlot)
            .ToListAsync(ct);

        return items;
    }
}
