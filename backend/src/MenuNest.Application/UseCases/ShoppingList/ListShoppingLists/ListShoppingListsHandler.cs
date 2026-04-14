using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;

public sealed class ListShoppingListsHandler
    : IQueryHandler<ListShoppingListsQuery, IReadOnlyList<ShoppingListDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListShoppingListsHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<ShoppingListDto>> Handle(
        ListShoppingListsQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var q = _db.ShoppingLists
            .Include(l => l.Items)
            .Where(l => l.FamilyId == familyId);

        if (query.Status.HasValue)
            q = q.Where(l => l.Status == query.Status.Value);

        return await q
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new ShoppingListDto(
                l.Id, l.Name, l.Status,
                l.Items.Count,
                l.Items.Count(i => i.IsBought),
                l.CreatedAt, l.CompletedAt))
            .ToListAsync(ct);
    }
}
