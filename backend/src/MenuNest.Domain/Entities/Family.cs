using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A family is the tenancy boundary: every domain resource belongs to
/// exactly one family and is only visible to its members.
/// </summary>
public sealed class Family : Entity
{
    public string Name { get; private set; } = null!;
    public InviteCode InviteCode { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }

    private readonly List<User> _members = new();
    public IReadOnlyCollection<User> Members => _members.AsReadOnly();

    // EF Core
    private Family() { }

    public static Family CreateNew(string name, Guid createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Family name cannot be empty.");
        }

        return new Family
        {
            Name = name.Trim(),
            InviteCode = InviteCode.Generate(),
            CreatedByUserId = createdByUserId
        };
    }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new DomainException("Family name cannot be empty.");
        }

        Name = newName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public InviteCode RotateInviteCode()
    {
        InviteCode = InviteCode.Generate();
        UpdatedAt = DateTime.UtcNow;
        return InviteCode;
    }
}
