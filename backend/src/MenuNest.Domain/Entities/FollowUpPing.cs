using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A scheduled "are you OK?" check-in for an active symptom episode.
/// The <c>FollowUpDispatcher</c> background service picks up Pending rows
/// whose <see cref="ScheduledAt"/> has arrived and sends a web push.
/// State machine: Pending → Asked → (Answered | Missed).
/// </summary>
public sealed class FollowUpPing : Entity
{
    public Guid SymptomEpisodeId { get; private set; }
    public DateTime ScheduledAt { get; private set; }
    public DateTime? AskedAt { get; private set; }
    public DateTime? RespondedAt { get; private set; }
    public PingResponse? Response { get; private set; }
    public int? SeverityAtCheck { get; private set; }
    public PingStatus Status { get; private set; } = PingStatus.Pending;

    // EF Core
    private FollowUpPing() { }

    public static FollowUpPing Schedule(Guid symptomEpisodeId, DateTime scheduledAt)
    {
        if (symptomEpisodeId == Guid.Empty)
            throw new DomainException("SymptomEpisodeId is required.");

        return new FollowUpPing
        {
            SymptomEpisodeId = symptomEpisodeId,
            ScheduledAt = scheduledAt
        };
    }

    public void MarkAsked()
    {
        if (Status != PingStatus.Pending)
            throw new DomainException($"Cannot mark ping as Asked from status {Status}.");

        Status = PingStatus.Asked;
        AskedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordResponse(PingResponse response, int? severityAtCheck = null)
    {
        if (Status == PingStatus.Answered)
            throw new DomainException("Ping is already answered.");
        if (severityAtCheck.HasValue && (severityAtCheck.Value < 0 || severityAtCheck.Value > 10))
            throw new DomainException("Severity-at-check must be between 0 and 10.");

        Response = response;
        SeverityAtCheck = severityAtCheck;
        RespondedAt = DateTime.UtcNow;
        Status = PingStatus.Answered;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkMissed()
    {
        if (Status == PingStatus.Answered)
            return; // idempotent — answered pings can never become missed

        Status = PingStatus.Missed;
        UpdatedAt = DateTime.UtcNow;
    }
}
