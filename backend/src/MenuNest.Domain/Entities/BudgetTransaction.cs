using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A spending/income event. <c>Amount</c> is stored as a signed decimal:
/// outflow (expense) is negative; inflow (income) is positive.
/// Always adds <c>Amount</c> to the account's balance — positive values (inflow) increase it, negative values (outflow) reduce it.
/// </summary>
public sealed class BudgetTransaction : Entity
{
    public Guid FamilyId { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? CategoryId { get; private set; } // null when it's an income/transfer inflow to "Ready to Assign"
    public decimal Amount { get; private set; }
    public DateOnly Date { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    /// <summary>
    /// Shared by the two legs of one payment (menunest-204, menunest-209), so the
    /// pair is found, edited and deleted as one row. Pairing only — it carries no
    /// arithmetic weight, which is why payments written before this feature
    /// shipped still compute correctly (spec §4.2).
    /// </summary>
    public Guid? PaymentId { get; private set; }

    private BudgetTransaction() { }

    public static BudgetTransaction Create(
        Guid familyId, Guid accountId, Guid? categoryId,
        decimal amount, DateOnly date, string? notes, Guid createdByUserId)
    {
        if (amount == 0) throw new DomainException("Transaction amount cannot be zero.");
        return new BudgetTransaction
        {
            FamilyId = familyId,
            AccountId = accountId,
            CategoryId = categoryId,
            Amount = amount,
            Date = date,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedByUserId = createdByUserId
        };
    }

    /// <summary>
    /// <paramref name="categoryId"/> (menunest-214) is null on both legs of a Credit
    /// payment — the Payment envelope already falls by derivation
    /// (<c>PaymentEnvelopeMath</c>), so categorising either leg would double-count
    /// it. It is REQUIRED on the outflow leg of a Loan payment: a Loan has no
    /// Payment envelope of its own (menunest-206), so the outflow leg's Envelope is
    /// the only thing that ever gets spent by paying a loan — without it the
    /// instalment drains Ready to Assign every month while the Envelope funding it
    /// is never touched (see menunest-214). The inflow leg, into the debt account
    /// itself, is ALWAYS null on both Credit and Loan: a payment landing on the
    /// debt account is never itself "spending".
    /// </summary>
    public static BudgetTransaction CreatePaymentLeg(
        Guid familyId, Guid accountId, Guid? categoryId, decimal amount, DateOnly date,
        string? notes, Guid createdByUserId, Guid paymentId)
    {
        if (amount == 0) throw new DomainException("Transaction amount cannot be zero.");
        if (paymentId == Guid.Empty) throw new DomainException("PaymentId is required.");
        return new BudgetTransaction
        {
            FamilyId = familyId,
            AccountId = accountId,
            CategoryId = categoryId,
            Amount = amount,
            Date = date,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedByUserId = createdByUserId,
            PaymentId = paymentId
        };
    }

    public void Update(Guid accountId, Guid? categoryId, decimal amount, DateOnly date, string? notes)
    {
        if (amount == 0) throw new DomainException("Transaction amount cannot be zero.");
        AccountId = accountId;
        CategoryId = categoryId;
        Amount = amount;
        Date = date;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
