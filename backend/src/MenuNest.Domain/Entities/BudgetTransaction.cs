using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A spending/income event. <c>Amount</c> is stored as a signed decimal:
/// outflow (expense) is negative; inflow (income) is positive.
/// Always debits the account balance by <c>Amount</c>.
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
