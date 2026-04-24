using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Transactions;

/// <summary>
/// Shared projection that hydrates a <see cref="BudgetTransactionDto"/> by id.
/// Used by Create/Update handlers so they return the same shape the list
/// query produces, without duplicating the join.
/// </summary>
internal static class TransactionDtoQuery
{
    public static async Task<BudgetTransactionDto> ByIdAsync(
        IApplicationDbContext db, Guid id, CancellationToken ct)
    {
        return await (
            from t in db.BudgetTransactions
            join a in db.BudgetAccounts on t.AccountId equals a.Id
            join u in db.Users on t.CreatedByUserId equals u.Id
            join c in db.BudgetCategories on t.CategoryId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            where t.Id == id
            select new BudgetTransactionDto(
                t.Id, t.AccountId, a.Name,
                t.CategoryId, c != null ? c.Name : null, c != null ? c.Emoji : null,
                t.Amount, t.Date, t.Notes,
                t.CreatedByUserId, u.DisplayName))
            .FirstAsync(ct);
    }
}
