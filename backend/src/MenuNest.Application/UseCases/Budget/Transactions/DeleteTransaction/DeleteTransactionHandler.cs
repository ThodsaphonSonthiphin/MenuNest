using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Transactions.DeleteTransaction;

public sealed class DeleteTransactionHandler : ICommandHandler<DeleteTransactionCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public DeleteTransactionHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(DeleteTransactionCommand c, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var tx = await _db.BudgetTransactions.FirstOrDefaultAsync(
            t => t.Id == c.Id && t.FamilyId == familyId, ct)
            ?? throw new DomainException("Transaction not found.");

        var acc = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == tx.AccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account not found.");

        acc.AdjustBalance(-tx.Amount);
        _db.BudgetTransactions.Remove(tx);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
