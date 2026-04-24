using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts.ListAccounts;

public sealed class ListAccountsHandler : IQueryHandler<ListAccountsQuery, IReadOnlyList<BudgetAccountDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public ListAccountsHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<IReadOnlyList<BudgetAccountDto>> Handle(ListAccountsQuery q, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        return await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId)
            .OrderBy(a => a.IsClosed).ThenBy(a => a.Type).ThenBy(a => a.SortOrder).ThenBy(a => a.Name)
            .Select(a => new BudgetAccountDto(a.Id, a.Name, a.Type, a.Balance, a.SortOrder, a.IsClosed))
            .ToListAsync(ct);
    }
}
