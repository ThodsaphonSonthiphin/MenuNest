using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.FollowUps.RecordPingResponse;

/// <summary>
/// Records a follow-up ping response. Behaviour by <see cref="PingResponse"/>:
/// <list type="bullet">
///   <item><c>Resolved</c> / <c>RetroResolved</c> — closes the episode
///   and marks all remaining pending pings missed.</item>
///   <item><c>Improved</c> / <c>Same</c> / <c>Worse</c> — schedules another
///   ping at <c>NOW + 30 min</c>, but only if fewer than 3 total pings
///   already exist for the episode (anti-spam cap).</item>
///   <item><c>RetroUnknown</c> — recorded; no reschedule, no resolve.</item>
/// </list>
/// </summary>
public sealed class RecordPingResponseHandler : ICommandHandler<RecordPingResponseCommand, Unit>
{
    private const int MaxPingsPerEpisode = 3;

    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<RecordPingResponseCommand> _validator;
    private readonly IClock _clock;

    public RecordPingResponseHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<RecordPingResponseCommand> validator,
        IClock clock)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
        _clock = clock;
    }

    public async ValueTask<Unit> Handle(RecordPingResponseCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var ping = await _db.FollowUpPings
            .FirstOrDefaultAsync(p => p.Id == command.PingId, ct)
            ?? throw new DomainException("Ping not found.");

        // Authorise via the parent episode so a user can't answer
        // someone else's ping.
        var episode = await _db.SymptomEpisodes
            .FirstOrDefaultAsync(e => e.Id == ping.SymptomEpisodeId, ct)
            ?? throw new DomainException("Ping not found.");
        if (episode.UserId != user.Id)
            throw new DomainException("Ping not found.");

        // Idempotent transition: only flip Pending → Asked if we haven't
        // already. Dispatcher may have marked it Asked already.
        if (ping.Status == PingStatus.Pending)
            ping.MarkAsked();

        ping.RecordResponse(command.Response, command.SeverityAtCheck);

        var now = _clock.UtcNow;

        if (command.Response == PingResponse.Resolved
            || command.Response == PingResponse.RetroResolved)
        {
            // Close the episode (idempotent: only if still active).
            if (episode.EndedAt is null)
                episode.Resolve(endedAt: now, severityAfter: 0);

            // Cancel any remaining pending pings on this episode.
            var pendingPings = await _db.FollowUpPings
                .Where(p => p.SymptomEpisodeId == episode.Id
                    && p.Status == PingStatus.Pending)
                .ToListAsync(ct);
            foreach (var p in pendingPings)
                p.MarkMissed();
        }
        else if (command.Response == PingResponse.Improved
            || command.Response == PingResponse.Same
            || command.Response == PingResponse.Worse)
        {
            // Reschedule another check-in, capped at 3 pings per episode.
            var totalPings = await _db.FollowUpPings
                .CountAsync(p => p.SymptomEpisodeId == episode.Id, ct);
            if (totalPings < MaxPingsPerEpisode)
            {
                var next = FollowUpPing.Schedule(episode.Id, now.AddMinutes(30));
                _db.FollowUpPings.Add(next);
            }
        }
        // RetroUnknown: nothing else to do — answer recorded.

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
