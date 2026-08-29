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

    /// <summary>
    /// The member who may undo any other member's budget change (menunest-198).
    /// The app's ONLY permission distinction, and it unlocks exactly that one
    /// power (menunest-201).
    ///
    /// <para>Nullable because a Family can legitimately have no head: its last
    /// member left, and the next person to join takes the role.</para>
    ///
    /// <para>Deliberately NOT <see cref="CreatedByUserId"/>: that records who
    /// happened to create the Family, and LeaveFamily never clears it, so it
    /// can already point at somebody who left.</para>
    /// </summary>
    public Guid? HeadUserId { get; private set; }

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
            CreatedByUserId = createdByUserId,
            HeadUserId = createdByUserId
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

    /// <summary>Hands the role on. Only the current head may do this (menunest-201).</summary>
    public void TransferHeadTo(Guid currentHeadUserId, Guid newHeadUserId)
    {
        if (HeadUserId != currentHeadUserId)
        {
            throw new DomainException("Only the family head can hand the role over.");
        }

        if (newHeadUserId == currentHeadUserId)
        {
            throw new DomainException("That member is already the family head.");
        }

        if (newHeadUserId == Guid.Empty)
        {
            throw new DomainException("A new head is required.");
        }

        HeadUserId = newHeadUserId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Gives a headless Family a head — the next joiner (menunest-201 rule 4).</summary>
    public void AssignHead(Guid userId)
    {
        if (HeadUserId is not null)
        {
            throw new DomainException("This family already has a head.");
        }

        if (userId == Guid.Empty)
        {
            throw new DomainException("A head is required.");
        }

        HeadUserId = userId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Leaves the Family headless — only when its last member leaves.</summary>
    public void ClearHead()
    {
        HeadUserId = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
