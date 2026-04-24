using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts.DeleteAccount;

public sealed class DeleteAccountHandler : ICommandHandler<DeleteAccountCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public DeleteAccountHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(DeleteAccountCommand c, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        var acc = await _db.BudgetAccounts.FirstOrDefaultAsync(a => a.Id == c.Id && a.FamilyId == familyId, ct)
                  ?? throw new DomainException("Account not found.");
        var hasTx = await _db.BudgetTransactions.AnyAsync(t => t.AccountId == c.Id, ct);
        if (hasTx) throw new DomainException("Cannot delete account with transactions — close it instead.");
        _db.BudgetAccounts.Remove(acc);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
