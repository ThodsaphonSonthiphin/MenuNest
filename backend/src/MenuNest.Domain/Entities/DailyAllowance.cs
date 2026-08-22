using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// The frozen "you can spend this much today" figure for a family (menunest-181).
/// Exactly one row per family, overwritten at every Budgeting event (menunest-185).
/// <para>
/// <c>FrozenPot</c> is not redundant with <c>Amount</c>: the Pace line measures
/// actually-spent as <c>FrozenPot - currentPot</c>. It cannot sum transactions
/// dated on or after <c>FrozenOn</c>, because <see cref="BudgetTransaction.Date"/>
/// is a <see cref="DateOnly"/> — a spend made earlier on the freeze day carries the
/// same date and would be double-counted (menunest-186).
/// </para>
/// </summary>
public sealed class DailyAllowance : Entity
{
    public Guid FamilyId { get; private set; }

    /// <summary>The frozen figure. Floors at 0; never moved by spending.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The everyday pot as it stood at the freeze. May be negative.</summary>
    public decimal FrozenPot { get; private set; }

    public DateOnly FrozenOn { get; private set; }
    public int ForYear { get; private set; }
    public int ForMonth { get; private set; }

    private DailyAllowance() { }

    public static DailyAllowance Freeze(Guid familyId, decimal pot, DateOnly on)
    {
        var allowance = new DailyAllowance { FamilyId = familyId };
        allowance.Refreeze(pot, on);
        return allowance;
    }

    public void Refreeze(decimal pot, DateOnly on)
    {
        var daysRemaining = DateTime.DaysInMonth(on.Year, on.Month) - on.Day + 1;
        if (daysRemaining <= 0)
            throw new DomainException("Days remaining in the month must be positive.");

        Amount = Math.Max(0m, pot / daysRemaining);
        FrozenPot = pot;
        FrozenOn = on;
        ForYear = on.Year;
        ForMonth = on.Month;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Whole days finished since the freeze. Zero on the freeze day itself, so the
    /// Pace line stays silent that day (menunest-186).
    /// </summary>
    public int CompletedDays(DateOnly today) => Math.Max(0, today.DayNumber - FrozenOn.DayNumber);

    /// <summary>
    /// The Pace line figure: actually-spent minus should-have-spent. Positive is
    /// "over", negative is "under", zero renders nothing (menunest-186).
    /// <para>
    /// Actually-spent is measured pot-against-pot, never by summing transactions
    /// dated on or after <see cref="FrozenOn"/> — see the class remarks.
    /// </para>
    /// </summary>
    public decimal PaceDelta(decimal currentPot, DateOnly today)
        => (FrozenPot - currentPot) - (Amount * CompletedDays(today));

    public bool IsForMonth(int year, int month) => ForYear == year && ForMonth == month;
}
