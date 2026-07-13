using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// The attachment of one ChecklistItem to a PlaceProfile master (the remembered "things to bring"
/// item-SET). No checked state — that is per-trip on PlaceChecklistEntry (ADR-059/064).
/// </summary>
public sealed class PlaceProfileChecklistItem : Entity
{
    public Guid PlaceProfileId { get; private set; }
    public Guid ChecklistItemId { get; private set; }

    private PlaceProfileChecklistItem() { } // EF

    public static PlaceProfileChecklistItem Create(Guid placeProfileId, Guid checklistItemId)
    {
        if (placeProfileId == Guid.Empty) throw new DomainException("PlaceProfileId is required.");
        if (checklistItemId == Guid.Empty) throw new DomainException("ChecklistItemId is required.");
        return new PlaceProfileChecklistItem { PlaceProfileId = placeProfileId, ChecklistItemId = checklistItemId };
    }
}
