using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Transactions.CreateTransaction;

public sealed class CreateTransactionHandler
    : ICommandHandler<CreateTransactionCommand, BudgetTransactionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CreateTransactionCommand> _v;
    public CreateTransactionHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<CreateTransactionCommand> v)
    { _db = db; _users = users; _v = v; }

    public async ValueTask<BudgetTransactionDto> Handle(CreateTransactionCommand c, CancellationToken ct)
    {
        await _v.ValidateAndThrowAsync(c, ct);
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var acc = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == c.AccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account not found.");

        // menunest-203 / OrdinaryEnvelopeRule: the category must exist, belong to
        // this family, and NOT be a Payment envelope. Without the third clause a
        // row categorised to a Payment envelope is invisible to both halves of the
        // model — the card's derivation (the row is on another account) and the
        // ordinary envelope walk (EnvelopeNumbers takes the payment branch) — while
        // the paying account's balance still falls. The money would leave Ready to
        // Assign and land in no envelope at all, with no error.
        if (c.CategoryId is { } categoryId)
        {
            _ = await OrdinaryEnvelopeRule.FindAsync(_db, categoryId, familyId, ct)
                ?? throw new DomainException(OrdinaryEnvelopeRule.TransactionRefusal);
        }

        var tx = BudgetTransaction.Create(familyId, c.AccountId, c.CategoryId, c.Amount, c.Date, c.Notes, user.Id);
        _db.BudgetTransactions.Add(tx);
        acc.AdjustBalance(c.Amount);
        await _db.SaveChangesAsync(ct);

        return await TransactionDtoQuery.ByIdAsync(_db, tx.Id, ct);
    }
}
