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

        // menunest-209: a payment is ONE row to the user. Editing one leg would
        // leave the debt paid in the budget and unpaid on the card.
        if (tx.PaymentId is not null)
            throw new DomainException(
                "This is a payment — edit it from the payment, not one side of it.");

        // Validate new category if provided. menunest-203 / OrdinaryEnvelopeRule:
        // exists, belongs to this family, and is NOT a Payment envelope. The edit
        // path needs the clause exactly as much as the create path — re-categorising
        // an ordinary row onto a Payment envelope makes the same money vanish, and
        // is the easier of the two to reach by accident.
        if (c.CategoryId is { } categoryId)
        {
            _ = await OrdinaryEnvelopeRule.FindAsync(_db, categoryId, familyId, ct)
                ?? throw new DomainException(OrdinaryEnvelopeRule.TransactionRefusal);
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
