using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.ListChecklistItems;

public sealed class ListChecklistItemsHandler : IQueryHandler<ListChecklistItemsQuery, IReadOnlyList<ChecklistItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public ListChecklistItemsHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<IReadOnlyList<ChecklistItemDto>> Handle(ListChecklistItemsQuery q, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        return await _db.ChecklistItems
            .Where(i => i.UserId == user.Id)
            .OrderBy(i => i.Name)
            .Select(i => new ChecklistItemDto(i.Id, i.Name))
            .ToListAsync(ct);
    }
}