using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Episodes.DeleteEpisode;

/// <summary>
/// HARD-deletes a symptom episode. SymptomEpisode has no <c>DeletedAt</c>
/// column so soft delete isn't an option; EF cascade rules (FollowUpPings
/// cascade, Intakes set <c>SymptomEpisodeId</c> to NULL) handle the
/// dependent rows automatically.
/// </summary>
public sealed class DeleteEpisodeHandler : ICommandHandler<DeleteEpisodeCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteEpisodeHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteEpisodeCommand command, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var episode = await _db.SymptomEpisodes
            .FirstOrDefaultAsync(e => e.Id == command.Id && e.UserId == user.Id, ct)
            ?? throw new DomainException("Episode not found.");

        _db.SymptomEpisodes.Remove(episode);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
