using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Transactions.UpdateTransaction;

public sealed class UpdateTransactionHandler
    : ICommandHandler<UpdateTransactionCommand, BudgetTransactionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdateTransactionCommand> _v;
    public UpdateTransactionHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdateTransactionCommand> v)
    { _db = db; _users = users; _v = v; }

    public async ValueTask<BudgetTransactionDto> Handle(UpdateTransactionCommand c, CancellationToken ct)
    {
        await _v.ValidateAndThrowAsync(c, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        // Load the existing transaction, scoped to the current family.
        var tx = await _db.BudgetTransactions.FirstOrDefaultAsync(
            t => t.Id == c.Id && t.FamilyId == familyId, ct)
            ?? throw new DomainException("Transaction not found.");

        // Validate new category if provided.
        if (c.CategoryId.HasValue)
        {
            var catOk = await _db.BudgetCategories.AnyAsync(
                x => x.Id == c.CategoryId && x.FamilyId == familyId, ct);
            if (!catOk) throw new DomainException("Category not found.");
        }

        // Balance math:
        //  - Same account: net delta = newAmount - oldAmount, applied once.
        //  - Cross account: reverse old delta on old account, apply new delta on new account.
        var oldAmount = tx.Amount;
        var oldAccountId = tx.AccountId;

        if (oldAccountId == c.AccountId)
        {
            var acc = await _db.BudgetAccounts.FirstOrDefaultAsync(
                a => a.Id == c.AccountId && a.FamilyId == familyId, ct)
                ?? throw new DomainException("Account not found.");
            acc.AdjustBalance(c.Amount - oldAmount);
        }
        else
        {
            var oldAcc = await _db.BudgetAccounts.FirstOrDefaultAsync(
                a => a.Id == oldAccountId && a.FamilyId == familyId, ct)
                ?? throw new DomainException("Account not found.");
            var newAcc = await _db.BudgetAccounts.FirstOrDefaultAsync(
                a => a.Id == c.AccountId && a.FamilyId == familyId, ct)
                ?? throw new DomainException("Account not found.");
            oldAcc.AdjustBalance(-oldAmount);
            newAcc.AdjustBalance(c.Amount);
        }

        tx.Update(c.AccountId, c.CategoryId, c.Amount, c.Date, c.Notes);
        await _db.SaveChangesAsync(ct);

        return await TransactionDtoQuery.ByIdAsync(_db, tx.Id, ct);
    }
}
