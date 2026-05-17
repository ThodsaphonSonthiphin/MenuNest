using Mediator;

namespace MenuNest.Application.UseCases.Health.FollowUps.GetPendingPings;

/// <summary>
/// Returns due follow-up pings (status = Pending, scheduled_at &lt;= now)
/// ordered by <c>ScheduledAt</c> ASC. Consumed by the
/// <c>FollowUpDispatcher</c> background service to drive web-push sends.
/// This query is system-context: it does NOT scope to the current user,
/// because the dispatcher runs outside any HTTP request.
/// </summary>
public sealed record GetPendingPingsQuery(int Limit = 50)
    : IQuery<IReadOnlyList<PendingPingDto>>;
