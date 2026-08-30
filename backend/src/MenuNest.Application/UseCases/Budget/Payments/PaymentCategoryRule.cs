using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Payments;

/// <summary>
/// menunest-214 / R-3: the outflow leg's category rule, shared by
/// MakePaymentHandler and UpdatePaymentHandler so a payment can only ever be
/// checked ONE way — an edit that reimplemented this separately is exactly
/// how the Critical defect (menunest-214) could silently come back on a Loan
/// payment's edit path.
///
/// <b>Loan</b>: <paramref name="categoryId"/> is REQUIRED — a Loan has no
/// Payment envelope of its own (menunest-206), so the outflow leg's Envelope
/// is the only thing a loan payment ever spends. The lookup excludes a
/// Payment envelope (<c>PaymentForAccountId != null</c>): that envelope is
/// derived solely from its own card's rows, so a categorised row there would
/// vanish from every derivation and reproduce the original defect one level
/// down.
///
/// <b>Credit</b>: <paramref name="categoryId"/> must be null — the card's
/// Payment envelope already falls by derivation (<see cref="Monthly.PaymentEnvelopeMath"/>);
/// categorising the outflow leg too would double-spend one payment across
/// two envelopes.
/// </summary>
internal static class PaymentCategoryRule
{
    public static async Task<BudgetCategory?> ResolveAsync(
        IApplicationDbContext db, BudgetAccountType toType, Guid? categoryId,
        Guid familyId, CancellationToken ct)
    {
        if (toType == BudgetAccountType.Loan)
        {
            if (categoryId is not { } id)
                throw new DomainException("Paying a Loan requires an Envelope to fund it.");
            // The clause itself lives in OrdinaryEnvelopeRule so this rule and the
            // ordinary transaction handlers cannot drift apart — they refuse the
            // same thing for the same reason (menunest-203).
            return await OrdinaryEnvelopeRule.FindAsync(db, id, familyId, ct)
                ?? throw new DomainException(
                    "Category not found, or is a Payment envelope — a Payment envelope cannot fund another debt's payment.");
        }

        // toType == Credit here — the caller has already narrowed to Credit or Loan.
        if (categoryId is not null)
            throw new DomainException(
                "Paying a Credit card cannot be categorised — its Payment envelope already falls by derivation.");

        return null;
    }
}
