using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// One recorded budget act, holding the DELTA it applied rather than the
/// values before and after (menunest-193). Undo issues the opposite delta as
/// a new write, so a concurrent change by another <see cref="Family"/> member
/// survives it; restoring a stored old value would silently destroy that
/// member's work.
///
/// <para><c>BatchId</c> groups the N writes one press of quick-assign makes
/// into a single history row (menunest-196).</para>
///
/// <para><c>Year</c>/<c>Month</c> is the BUDGET month the act belongs to, not
/// the wall clock — menunest-194 cuts the visible window at the start of the
/// current budget month, so this is what that filter reads.</para>
/// </summary>
public sealed class BudgetChange : Entity
{
    public Guid FamilyId { get; private set; }

    /// <summary>
    /// Who performed the act. menunest-198: a member may undo their own, the
    /// family head may undo anyone's.
    /// </summary>
    public Guid UserId { get; private set; }

    public int Year { get; private set; }
    public int Month { get; private set; }
    public BudgetChangeKind Kind { get; private set; }

    /// <summary>Non-null when this row is one of N writes from a single quick-assign press.</summary>
    public Guid? BatchId { get; private set; }

    /// <summary>The envelope the delta was applied to. For Move/Cover this is the SOURCE.</summary>
    public Guid CategoryId { get; private set; }

    /// <summary>Move/Cover only: the destination envelope, which received the opposite delta.</summary>
    public Guid? SecondCategoryId { get; private set; }

    /// <summary>Signed amount added to <see cref="CategoryId"/>. Zero for EverydayMark.</summary>
    public decimal Delta { get; private set; }

    /// <summary>EverydayMark only: the value the mark was set TO.</summary>
    public bool? FlagValue { get; private set; }

    public bool IsUndone { get; private set; }
    public Guid? UndoneByUserId { get; private set; }
    public DateTime? UndoneAt { get; private set; }

    // EF Core
    private BudgetChange() { }

    public static BudgetChange RecordAssign(
        Guid familyId, Guid userId, int year, int month,
        Guid categoryId, decimal delta, Guid? batchId)
    {
        if (delta == 0m) throw new DomainException("An assign with no effect is not recorded.");
        return New(familyId, userId, year, month, BudgetChangeKind.Assign, batchId,
                   categoryId, null, delta, null);
    }

    public static BudgetChange RecordMove(
        Guid familyId, Guid userId, int year, int month,
        Guid fromCategoryId, Guid toCategoryId, decimal amount, bool isCover)
    {
        if (amount <= 0m) throw new DomainException("A move must carry a positive amount.");
        if (fromCategoryId == toCategoryId) throw new DomainException("A move needs two different envelopes.");
        return New(familyId, userId, year, month,
                   isCover ? BudgetChangeKind.Cover : BudgetChangeKind.Move, null,
                   fromCategoryId, toCategoryId, -amount, null);
    }

    public static BudgetChange RecordEverydayMark(
        Guid familyId, Guid userId, int year, int month, Guid categoryId, bool newValue)
        => New(familyId, userId, year, month, BudgetChangeKind.EverydayMark, null,
               categoryId, null, 0m, newValue);

    private static BudgetChange New(
        Guid familyId, Guid userId, int year, int month, BudgetChangeKind kind,
        Guid? batchId, Guid categoryId, Guid? secondCategoryId, decimal delta, bool? flagValue)
    {
        if (familyId == Guid.Empty) throw new DomainException("FamilyId is required.");
        if (userId == Guid.Empty) throw new DomainException("UserId is required.");
        if (categoryId == Guid.Empty) throw new DomainException("CategoryId is required.");
        if (year < 2000 || year > 2100) throw new DomainException("Invalid year.");
        if (month < 1 || month > 12) throw new DomainException("Invalid month.");

        return new BudgetChange
        {
            FamilyId = familyId,
            UserId = userId,
            Year = year,
            Month = month,
            Kind = kind,
            BatchId = batchId,
            CategoryId = categoryId,
            SecondCategoryId = secondCategoryId,
            Delta = delta,
            FlagValue = flagValue,
            IsUndone = false,
        };
    }

    public void MarkUndone(Guid byUserId, DateTime atUtc)
    {
        if (IsUndone) throw new DomainException("This change is already undone.");
        IsUndone = true;
        UndoneByUserId = byUserId;
        UndoneAt = atUtc;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkRedone()
    {
        if (!IsUndone) throw new DomainException("This change is not undone.");
        IsUndone = false;
        UndoneByUserId = null;
        UndoneAt = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
