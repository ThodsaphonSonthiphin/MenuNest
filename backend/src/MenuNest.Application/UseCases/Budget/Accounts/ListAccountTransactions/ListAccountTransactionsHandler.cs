using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts.ListAccountTransactions;

public sealed class ListAccountTransactionsHandler
    : IQueryHandler<ListAccountTransactionsQuery, AccountTransactionsPageDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public ListAccountTransactionsHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<AccountTransactionsPageDto> Handle(
        ListAccountTransactionsQuery q, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var acct = await _db.BudgetAccounts
            .FirstOrDefaultAsync(a => a.Id == q.AccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account not found.");

        var skip = Math.Max(q.Skip, 0);
        var take = Math.Clamp(q.Take, 1, 100);

        var inflow = await _db.BudgetTransactions
            .Where(t => t.AccountId == acct.Id
                     && t.Date.Year == q.Year && t.Date.Month == q.Month
                     && t.Amount > 0)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
        var outflow = await _db.BudgetTransactions
            .Where(t => t.AccountId == acct.Id
                     && t.Date.Year == q.Year && t.Date.Month == q.Month
                     && t.Amount < 0)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var pageQuery =
            from t in _db.BudgetTransactions
            join a in _db.BudgetAccounts on t.AccountId equals a.Id
            join u in _db.Users on t.CreatedByUserId equals u.Id
            join c in _db.BudgetCategories on t.CategoryId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            where t.AccountId == acct.Id
            orderby t.CreatedAt descending
            select new BudgetTransactionDto(
                t.Id, t.AccountId, a.Name,
                t.CategoryId, c != null ? c.Name : null, c != null ? c.Emoji : null,
                t.Amount, t.Date, t.Notes,
                t.CreatedByUserId, u.DisplayName);

        var items = await pageQuery.Skip(skip).Take(take).ToListAsync(ct);
        var hasMore = await pageQuery.Skip(skip + take).AnyAsync(ct);

        return new AccountTransactionsPageDto(
            new AccountSummaryDto(acct.Id, acct.Name, acct.Type, acct.Balance, inflow, outflow),
            items,
            hasMore);
    }
}
