using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// Per-user preferences (1:1 with <see cref="User"/>). Created lazily on
/// first write. Holds the user's chosen Home page route plus their
/// UV-index and feels-like weather-warning thresholds.
/// </summary>
public sealed class UserSettings : Entity
{
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;

    /// <summary>The route "/" resolves to, e.g. "/pomodoro". Null = unset.</summary>
    public string? HomePath { get; private set; }

    /// <summary>UV-index warn threshold. Null = default (6); 0 = off; N = warn at UV >= N.</summary>
    public int? UvWarnThreshold { get; private set; }
    /// <summary>Feels-like warn threshold in C. Null = default (40); 0 = off; N = warn at feels >= N.</summary>
    public int? FeelsLikeWarnThreshold { get; private set; }

    /// <summary>
    /// The one grammar rule the AI correction loop grades against, e.g.
    /// "third-person singular -s". Null = the writer has never chosen one;
    /// get_active_target_rule then returns null and Claude Code asks in chat
    /// before correcting (mcp-tool-contract). Flipped by hand, never on a
    /// calendar rotation (rule-rotation).
    /// </summary>
    public string? ActiveTargetRule { get; private set; }

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

    public void SetWeatherAlerts(int? uv, int? feels)
    {
        UvWarnThreshold = uv;
        FeelsLikeWarnThreshold = feels;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the active target grammar rule. Blank input clears it to unset,
    /// matching <see cref="SetHomePath"/>'s convention. Capped at 200 to match
    /// WritingEntry.TargetRule, which snapshots this value on every correction —
    /// a longer rule would set cleanly here and then fail when a correction
    /// copied it.
    /// </summary>
    public void SetActiveTargetRule(string? rule)
    {
        var trimmed = string.IsNullOrWhiteSpace(rule) ? null : rule.Trim();
        if (trimmed is not null && trimmed.Length > 200)
        {
            throw new DomainException("ActiveTargetRule must be 200 characters or less.");
        }

        ActiveTargetRule = trimmed;
        UpdatedAt = DateTime.UtcNow;
    }
}
