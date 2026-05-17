namespace MenuNest.Domain.Enums;

/// <summary>
/// Lifecycle of a follow-up ping. Background dispatcher picks up
/// <see cref="Pending"/> rows whose <c>ScheduledAt</c> has passed.
/// </summary>
public enum PingStatus
{
    Pending = 1,
    Asked = 2,
    Answered = 3,
    Missed = 4
}
