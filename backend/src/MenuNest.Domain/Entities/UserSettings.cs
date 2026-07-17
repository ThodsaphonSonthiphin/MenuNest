using MenuNest.Domain.Common;

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
        return new UserSettings { UserId = userId };
    }

    public void SetHomePath(string? homePath)
    {
        HomePath = string.IsNullOrWhiteSpace(homePath) ? null : homePath.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}