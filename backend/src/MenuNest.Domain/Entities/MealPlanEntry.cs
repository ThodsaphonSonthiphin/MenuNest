using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A single slot on the meal plan: one recipe for one family on one
/// date for one meal of the day. Tracks whether the meal has actually
/// been cooked so stock can be deducted and later undone.
/// </summary>
public sealed class MealPlanEntry : Entity
{
    public Guid FamilyId { get; private set; }
    public DateOnly Date { get; private set; }
    public MealSlot MealSlot { get; private set; }
    public Guid RecipeId { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public MealEntryStatus Status { get; private set; } = MealEntryStatus.Planned;
    public DateTime? CookedAt { get; private set; }
    public Guid? CookedByUserId { get; private set; }
    public string? CookNotes { get; private set; }

    // EF Core
    private MealPlanEntry() { }

    public static MealPlanEntry Create(
        Guid familyId,
        DateOnly date,
        MealSlot slot,
        Guid recipeId,
        Guid createdByUserId,
        string? notes = null)
    {
        return new MealPlanEntry
        {
            FamilyId = familyId,
            Date = date,
            MealSlot = slot,
            RecipeId = recipeId,
            Notes = notes?.Trim(),
            CreatedByUserId = createdByUserId
        };
    }

    public void ChangeRecipe(Guid recipeId)
    {
        if (Status == MealEntryStatus.Cooked)
        {
            throw new DomainException("Cannot change the recipe of a meal that has already been cooked. Undo first.");
        }

        RecipeId = recipeId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateNotes(string? notes)
    {
        Notes = notes?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCooked(Guid userId, string? cookNotes = null)
    {
        if (Status != MealEntryStatus.Planned)
        {
            throw new DomainException("Only planned entries can be cooked.");
        }

        Status = MealEntryStatus.Cooked;
        CookedAt = DateTime.UtcNow;
        CookedByUserId = userId;
        CookNotes = cookNotes?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void Uncook()
    {
        if (Status != MealEntryStatus.Cooked)
        {
            throw new DomainException("Entry has not been cooked.");
        }

        Status = MealEntryStatus.Planned;
        CookedAt = null;
        CookedByUserId = null;
        CookNotes = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Skip()
    {
        Status = MealEntryStatus.Skipped;
        UpdatedAt = DateTime.UtcNow;
    }
}
