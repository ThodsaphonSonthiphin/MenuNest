using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// One row per (Family, Category, Year, Month). Only stores the user-set
/// assigned amount; Activity and Available are computed from transactions
/// and prior-month rollover.
/// </summary>
public sealed class MonthlyAssignment : Entity
{
    public Guid FamilyId { get; private set; }
    public Guid CategoryId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public decimal AssignedAmount { get; private set; }

    private MonthlyAssignment() { }

    public static MonthlyAssignment Create(
        Guid familyId, Guid categoryId, int year, int month, decimal amount)
    {
        if (year < 2000 || year > 2100) throw new DomainException("Invalid year.");
        if (month < 1 || month > 12)    throw new DomainException("Invalid month.");
        return new MonthlyAssignment
        {
            FamilyId = familyId,
            CategoryId = categoryId,
            Year = year,
            Month = month,
            AssignedAmount = amount
        };
    }

    public void SetAmount(decimal amount)
    {
        AssignedAmount = amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AdjustAmount(decimal delta)
    {
        AssignedAmount += delta;
        UpdatedAt = DateTime.UtcNow;
    }
}
