using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A User-scoped, reusable checklist label ("thing to bring/prepare for a place",
/// e.g. ร่ม / พาสปอร์ต / ครีมกันแดด). Owned by the User (not a Trip/Place) so one item is
/// reused across many Places and Trips (ADR-058). Attached to a Place via PlaceChecklistEntry.
/// </summary>
public sealed class ChecklistItem : Entity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = null!;

    private ChecklistItem() { } // EF

    public static ChecklistItem Create(Guid userId, string name)
    {
        if (userId == Guid.Empty) throw new DomainException("UserId is required for a checklist item.");
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw new DomainException("Checklist item name is required.");
        if (n.Length > 100) throw new DomainException("Checklist item name is too long (max 100).");
        return new ChecklistItem { UserId = userId, Name = n };
    }
}