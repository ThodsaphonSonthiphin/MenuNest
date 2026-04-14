using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A MenuNest user. Identity is auto-provisioned from the Entra ID
/// <c>oid</c> claim on first sign-in. A user may belong to zero or one
/// family at a time.
/// </summary>
public sealed class User : Entity
{
    public string ExternalId { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;

    public Guid? FamilyId { get; private set; }
    public Family? Family { get; private set; }
    public DateTime? JoinedAt { get; private set; }
    public AuthProvider AuthProvider { get; private set; }

    // EF Core
    private User() { }

    public static User CreateFromExternalLogin(
        string externalId, string email, string displayName, AuthProvider authProvider)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new DomainException("ExternalId is required.");
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email is required.");

        return new User
        {
            ExternalId = externalId,
            Email = email.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Trim() : displayName.Trim(),
            AuthProvider = authProvider,
        };
    }

    public void UpdateProfile(string email, string displayName)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("Email is required.");
        }

        Email = email.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Trim() : displayName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void JoinFamily(Guid familyId)
    {
        if (FamilyId.HasValue)
        {
            throw new DomainException("User is already a member of a family. Leave first before joining another.");
        }

        FamilyId = familyId;
        JoinedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void LeaveFamily()
    {
        FamilyId = null;
        JoinedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
