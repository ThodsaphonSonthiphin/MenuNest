using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Transactions.ListTransactions;

public sealed class ListTransactionsHandler
    : IQueryHandler<ListTransactionsQuery, IReadOnlyList<BudgetTransactionDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public ListTransactionsHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<IReadOnlyList<BudgetTransactionDto>> Handle(
        ListTransactionsQuery q, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var query =
            from t in _db.BudgetTransactions
            join a in _db.BudgetAccounts on t.AccountId equals a.Id
            join u in _db.Users on t.CreatedByUserId equals u.Id
            join c in _db.BudgetCategories on t.CategoryId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            where t.FamilyId == familyId
               && t.Date.Year == q.Year
               && t.Date.Month == q.Month
               && (q.CategoryId == null || t.CategoryId == q.CategoryId)
            orderby t.Date descending, t.CreatedAt descending
            select new BudgetTransactionDto(
                t.Id, t.AccountId, a.Name,
                t.CategoryId, c != null ? c.Name : null, c != null ? c.Emoji : null,
                t.Amount, t.Date, t.Notes,
                t.CreatedByUserId, u.DisplayName);

        return await query.ToListAsync(ct);
    }
}
