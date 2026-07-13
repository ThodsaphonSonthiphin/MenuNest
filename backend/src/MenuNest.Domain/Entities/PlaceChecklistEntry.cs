using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// The attachment of one <see cref="ChecklistItem"/> to one Place (TripPlace), carrying a
/// per-Place <see cref="IsChecked"/> flag ("เตรียมแล้ว"). Detaching removes this row only,
/// never the library <see cref="ChecklistItem"/> (ADR-059).
/// </summary>
public sealed class PlaceChecklistEntry : Entity
{
    public Guid TripPlaceId { get; private set; }
    public Guid ChecklistItemId { get; private set; }
    public bool IsChecked { get; private set; }

    private PlaceChecklistEntry() { } // EF

    public static PlaceChecklistEntry Create(Guid tripPlaceId, Guid checklistItemId)
    {
        if (tripPlaceId == Guid.Empty) throw new DomainException("TripPlaceId is required.");
        if (checklistItemId == Guid.Empty) throw new DomainException("ChecklistItemId is required.");
        return new PlaceChecklistEntry { TripPlaceId = tripPlaceId, ChecklistItemId = checklistItemId, IsChecked = false };
    }

    public void SetChecked(bool isChecked)
    {
        IsChecked = isChecked;
        UpdatedAt = DateTime.UtcNow;
    }
}