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

        // menunest-202/210: a Credit account owns a Payment envelope, bound by
        // BudgetCategoryConfiguration's
        //   HasForeignKey(x => x.PaymentForAccountId).OnDelete(DeleteBehavior.Restrict)
        // Nothing above loads that envelope, so EF cannot sever the reference in
        // memory: it would issue the bare DELETE and the database would refuse it
        // with a FOREIGN KEY violation — an unhandled DbUpdateException, HTTP 500,
        // "An unexpected error occurred." on an UNUSED card, which deleted fine
        // before this feature existed. menunest-210 assumed "both go together
        // harmlessly"; they do not go together unless this handler takes the
        // envelope with the account, so it does.
        //
        // The envelope is provably empty of activity here — the account carries no
        // transactions, and a Payment envelope's Available is derived from its own
        // card's rows alone (PaymentEnvelopeMath) — so all that can remain is
        // assignments. Removing those returns their money to Ready to Assign, which
        // is menunest-210's stated behaviour for a card that no longer holds debt.
        var envelope = await _db.BudgetCategories
            .FirstOrDefaultAsync(x => x.PaymentForAccountId == c.Id && x.FamilyId == familyId, ct);
        if (envelope is not null)
        {
            // BudgetChange → BudgetCategory is Restrict on purpose (menunest-197: a
            // row whose Envelope was deleted must STAY on the history list, greyed,
            // saying why). Deleting the envelope out from under one would be the
            // same DbUpdateException-as-500 by another route, so refuse it in words
            // the SPA can show — exactly as DeleteCategoryHandler already does.
            // SecondCategoryId is checked too: a Move INTO this envelope names it
            // only there, and undoing that change later would resurrect an
            // assignment on a category that no longer exists.
            var hasHistory = await _db.BudgetChanges.AnyAsync(
                h => h.CategoryId == envelope.Id || h.SecondCategoryId == envelope.Id, ct);
            if (hasHistory)
                throw new DomainException(
                    "Cannot delete an account whose payment envelope has recent budget history — close it instead.");

            // MonthlyAssignment → BudgetCategory is Restrict as well, so these have
            // to go in the SAME unit of work or the envelope delete fails for a
            // second, different reason.
            var assignments = await _db.MonthlyAssignments
                .Where(a => a.CategoryId == envelope.Id).ToListAsync(ct);
            _db.MonthlyAssignments.RemoveRange(assignments);
            _db.BudgetCategories.Remove(envelope);
        }

        _db.BudgetAccounts.Remove(acc);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
