using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.FollowUps.RetroCloseEpisode;

public sealed class RetroCloseEpisodeHandler : ICommandHandler<RetroCloseEpisodeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<RetroCloseEpisodeCommand> _validator;
    private readonly IClock _clock;

    public RetroCloseEpisodeHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<RetroCloseEpisodeCommand> validator,
        IClock clock)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
        _clock = clock;
    }

    public async ValueTask<Unit> Handle(RetroCloseEpisodeCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        // Whitelist the two retro outcomes — any live-ping value would
        // bypass the retro-close path's intent.
        if (command.Outcome != PingResponse.RetroResolved
            && command.Outcome != PingResponse.RetroUnknown)
        {
            throw new DomainException(
                "Outcome must be RetroResolved or RetroUnknown.");
        }

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var episode = await _db.SymptomEpisodes
            .FirstOrDefaultAsync(e => e.Id == command.EpisodeId
                && e.UserId == user.Id, ct)
            ?? throw new DomainException("Episode not found.");

        if (episode.EndedAt is not null)
            throw new DomainException("Episode is already closed.");

        episode.RetroClose(
            estimatedDuration: command.EstimatedDuration,
            endedAt: _clock.UtcNow);

        // Cancel any remaining pending pings on this episode — the
        // dispatcher only excludes Answered, not Pending.
        var pendingPings = await _db.FollowUpPings
            .Where(p => p.SymptomEpisodeId == episode.Id
                && p.Status == PingStatus.Pending)
            .ToListAsync(ct);
        foreach (var p in pendingPings)
            p.MarkMissed();

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
