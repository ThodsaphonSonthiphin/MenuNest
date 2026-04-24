using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Groups.ListGroups;

public sealed class ListGroupsHandler : IQueryHandler<ListGroupsQuery, IReadOnlyList<CategoryGroupDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public ListGroupsHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<IReadOnlyList<CategoryGroupDto>> Handle(ListGroupsQuery q, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        return await _db.BudgetCategoryGroups
            .Where(g => g.FamilyId == familyId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .Select(g => new CategoryGroupDto(g.Id, g.Name, g.SortOrder, g.IsHidden))
            .ToListAsync(ct);
    }
}
