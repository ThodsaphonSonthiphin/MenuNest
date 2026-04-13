using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A directional relationship between two family members, stored as
/// metadata only — it has no effect on permissions.
/// </summary>
public sealed class UserRelationship : Entity
{
    public Guid FamilyId { get; private set; }
    public Guid FromUserId { get; private set; }
    public Guid ToUserId { get; private set; }
    public RelationType RelationType { get; private set; }

    // EF Core
    private UserRelationship() { }

    public static UserRelationship Create(
        Guid familyId,
        Guid fromUserId,
        Guid toUserId,
        RelationType relationType)
    {
        if (fromUserId == toUserId)
        {
            throw new DomainException("A user cannot have a relationship with themselves.");
        }

        return new UserRelationship
        {
            FamilyId = familyId,
            FromUserId = fromUserId,
            ToUserId = toUserId,
            RelationType = relationType
        };
    }

    public void ChangeType(RelationType newType)
    {
        RelationType = newType;
        UpdatedAt = DateTime.UtcNow;
    }
}
