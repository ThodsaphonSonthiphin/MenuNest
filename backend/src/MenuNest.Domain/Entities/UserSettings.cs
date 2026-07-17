using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// Per-user preferences (1:1 with <see cref="User"/>). Created lazily on
/// first write. Currently holds only the user's chosen Home page route.
/// </summary>
public sealed class UserSettings : Entity
{
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;

    /// <summary>The route "/" resolves to, e.g. "/pomodoro". Null = unset.</summary>
    public string? HomePath { get; private set; }

    // EF Core
    private UserSettings() { }

    public static UserSettings Create(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("UserId is required.");
        }

        return new UserSettings { UserId = userId };
    }

    public void SetHomePath(string? homePath)
    {
        var trimmed = string.IsNullOrWhiteSpace(homePath) ? null : homePath.Trim();
        if (trimmed is not null && trimmed.Length > 100)
        {
            throw new DomainException("HomePath must be 100 characters or less.");
        }

        HomePath = trimmed;
        UpdatedAt = DateTime.UtcNow;
    }
}
