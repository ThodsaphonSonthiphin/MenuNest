using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// Expected family income for a specific (Year, Month). Used as the seed
/// for "Ready to Assign" on top of any rollover from the previous month.
/// </summary>
public sealed class MonthlyIncome : Entity
{
    public Guid FamilyId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public decimal Amount { get; private set; }

    private MonthlyIncome() { }

    public static MonthlyIncome Create(Guid familyId, int year, int month, decimal amount)
    {
        if (year < 2000 || year > 2100) throw new DomainException("Invalid year.");
        if (month < 1 || month > 12)    throw new DomainException("Invalid month.");
        if (amount < 0)                 throw new DomainException("Income cannot be negative.");
        return new MonthlyIncome { FamilyId = familyId, Year = year, Month = month, Amount = amount };
    }

    public void SetAmount(decimal amount)
    {
        if (amount < 0) throw new DomainException("Income cannot be negative.");
        Amount = amount;
        UpdatedAt = DateTime.UtcNow;
    }
}
