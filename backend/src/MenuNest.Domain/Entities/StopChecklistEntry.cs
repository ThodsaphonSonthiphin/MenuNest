using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// The attachment of one <see cref="ChecklistItem"/> to one <see cref="Stop"/>, carrying a
/// per-Stop <see cref="IsChecked"/> flag ("เตรียมแล้ว"). Detaching removes this row only,
/// never the library <see cref="ChecklistItem"/> (ADR-059).
/// </summary>
public sealed class StopChecklistEntry : Entity
{
    public const int MaxPerStop = 20;

    public Guid StopId { get; private set; }
    public Guid ChecklistItemId { get; private set; }
    public bool IsChecked { get; private set; }

    private StopChecklistEntry() { } // EF

    public static StopChecklistEntry Create(Guid stopId, Guid checklistItemId)
    {
        if (stopId == Guid.Empty) throw new DomainException("StopId is required.");
        if (checklistItemId == Guid.Empty) throw new DomainException("ChecklistItemId is required.");
        return new StopChecklistEntry { StopId = stopId, ChecklistItemId = checklistItemId, IsChecked = false };
    }

    public void SetChecked(bool isChecked)
    {
        IsChecked = isChecked;
        UpdatedAt = DateTime.UtcNow;
    }
}