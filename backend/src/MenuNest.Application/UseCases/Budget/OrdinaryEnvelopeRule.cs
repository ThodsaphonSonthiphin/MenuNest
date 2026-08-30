using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget;

/// <summary>
/// The single clause behind "an ordinary Envelope": it exists, it belongs to
/// this Family, and it is <b>not</b> a Payment envelope
/// (<c>PaymentForAccountId == null</c>).
///
/// A Payment envelope's <b>Available</b> is DERIVED — <see cref="Monthly.PaymentEnvelopeMath"/>
/// reads only its own card's rows — and <c>GetMonthlySummaryHandler.EnvelopeNumbers</c>
/// branches on that, never walking the ordinary transaction list for one. So a
/// transaction categorised to a Payment envelope is seen by NEITHER half of the
/// model: not by the derivation (the row is on the wrong account) and not by the
/// ordinary walk (the envelope takes the other branch). The account's balance
/// still falls, so the money leaves Ready to Assign and lands nowhere —
/// menunest-203's own stated failure.
///
/// Every caller that accepts a category id from the User therefore has to apply
/// the same three-part clause, and it lives here so it can only ever be written
/// once: <see cref="Payments.PaymentCategoryRule"/> (a loan payment's funding
/// Envelope), and the ordinary create/update transaction handlers.
/// </summary>
internal static class OrdinaryEnvelopeRule
{
    /// <summary>
    /// The category, or <c>null</c> when it does not exist, belongs to another
    /// Family, or is a Payment envelope. Callers raise their own message — the
    /// three failures are deliberately indistinguishable to the caller so a
    /// probe cannot enumerate another Family's categories.
    /// </summary>
    public static async Task<BudgetCategory?> FindAsync(
        IApplicationDbContext db, Guid categoryId, Guid familyId, CancellationToken ct) =>
        await db.BudgetCategories.FirstOrDefaultAsync(
            x => x.Id == categoryId && x.FamilyId == familyId && x.PaymentForAccountId == null, ct);

    /// <summary>
    /// What an ordinary transaction handler says when the category fails the
    /// clause. It names the payment action, because "Category not found" on an
    /// envelope the User can plainly see in the picker reads as a bug.
    /// menunest-212's vocabulary: จ่ายบัตร / จ่ายค่างวด, never จ่ายหนี้ or ชำระ.
    /// </summary>
    public const string TransactionRefusal =
        "Category not found, or is a Payment envelope — money leaves a Payment envelope " +
        "only by paying the account it belongs to (จ่ายบัตร / จ่ายค่างวด), never by " +
        "categorising a transaction to it.";
}
