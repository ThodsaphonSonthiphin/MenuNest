using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Intakes.LogNoDrug;

public sealed class LogNoDrugHandler : ICommandHandler<LogNoDrugCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IClock _clock;

    public LogNoDrugHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IClock clock)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _clock = clock;
    }

    public async ValueTask<Unit> Handle(LogNoDrugCommand command, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var episode = await _db.SymptomEpisodes
            .FirstOrDefaultAsync(e => e.Id == command.SymptomEpisodeId
                && e.UserId == user.Id, ct)
            ?? throw new DomainException("Episode not found.");

        episode.MarkNoDrug(command.Reason);

        // Self-resolving follow-up at +60 min — the user might naturally
        // improve without medication, so we still want to capture outcome.
        var ping = FollowUpPing.Schedule(
            episode.Id,
            _clock.UtcNow.AddMinutes(60));
        _db.FollowUpPings.Add(ping);

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
