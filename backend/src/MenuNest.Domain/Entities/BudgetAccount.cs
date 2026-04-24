using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A family-scoped money container (cash, credit, loan). Tracks the current
/// balance; mutations flow through methods so invariants (non-empty name)
/// and <c>UpdatedAt</c> stay consistent.
/// </summary>
public sealed class BudgetAccount : Entity
{
    public Guid FamilyId { get; private set; }
    public string Name { get; private set; } = null!;
    public BudgetAccountType Type { get; private set; }
    public decimal Balance { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsClosed { get; private set; }

    private BudgetAccount() { }

    public static BudgetAccount Create(
        Guid familyId, string name, BudgetAccountType type,
        decimal openingBalance, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Account name is required.");

        return new BudgetAccount
        {
            FamilyId = familyId,
            Name = name.Trim(),
            Type = type,
            Balance = openingBalance,
            SortOrder = sortOrder,
            IsClosed = false
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Account name is required.");
        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AdjustBalance(decimal delta)
    {
        Balance += delta;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Directly overwrites the stored balance. Intended for reconciliation only;
    /// prefer <see cref="AdjustBalance"/> driven by <see cref="BudgetTransaction"/> records
    /// so transaction history stays consistent.
    /// </summary>
    public void SetBalance(decimal balance)
    {
        Balance = balance;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Close() { IsClosed = true; UpdatedAt = DateTime.UtcNow; }
    public void Reopen() { IsClosed = false; UpdatedAt = DateTime.UtcNow; }
}
