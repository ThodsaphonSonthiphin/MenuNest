using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Payments.DeletePayment;

/// <summary>
/// menunest-209: removes both legs of a payment. Restoring whatever the
/// payment spent falls entirely out of the derivation — nothing here writes
/// to an envelope:
///  - Card: the card-side leg was an uncategorised positive on a Credit
///    account, which <see cref="Monthly.PaymentEnvelopeMath"/> subtracts
///    from that card's Payment envelope. Removing it restores that envelope.
///  - Loan: the outflow leg was a categorised row, summed by the ordinary
///    envelope walk. Removing it restores that envelope's Available.
/// </summary>
public sealed class DeletePaymentHandler : ICommandHandler<DeletePaymentCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public DeletePaymentHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(DeletePaymentCommand c, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var legs = await _db.BudgetTransactions
            .Where(t => t.PaymentId == c.PaymentId && t.FamilyId == familyId)
            .ToListAsync(ct);
        if (legs.Count == 0)
            throw new DomainException("Payment not found.");

        foreach (var leg in legs)
        {
            var acc = await _db.BudgetAccounts.FirstOrDefaultAsync(
                a => a.Id == leg.AccountId && a.FamilyId == familyId, ct)
                ?? throw new DomainException("Account not found.");
            acc.AdjustBalance(-leg.Amount);
        }

        _db.BudgetTransactions.RemoveRange(legs);
        await _db.SaveChangesAsync(ct); // ONE unit of work — never half a pair
        return Unit.Value;
    }
}
