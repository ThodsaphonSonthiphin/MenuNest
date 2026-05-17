using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Episodes.StartEpisode;

public sealed class StartEpisodeHandler : ICommandHandler<StartEpisodeCommand, EpisodeDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<StartEpisodeCommand> _validator;

    public StartEpisodeHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<StartEpisodeCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<EpisodeDto> Handle(StartEpisodeCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var episode = SymptomEpisode.Start(
            userId: user.Id,
            symptomId: command.SymptomId,
            severity: command.Severity,
            isOnPeriod: command.IsOnPeriod,
            startedAt: command.StartedAt,
            triggerIds: command.TriggerIds,
            notes: command.Notes);

        // Set migraine attributes only when at least one is supplied — the
        // domain method always overwrites all of them so we skip the call
        // when nothing was provided to avoid clobbering future defaults.
        var hasAnyMigraineAttr =
            command.HasAura.HasValue
            || command.AuraTypes is { Count: > 0 }
            || command.AuraDurationMin.HasValue
            || command.Location.HasValue
            || command.Quality.HasValue
            || command.AssociatedSymptoms is { Count: > 0 }
            || command.WorsenedByActivity.HasValue
            || command.FunctionalImpact.HasValue;

        if (hasAnyMigraineAttr)
        {
            episode.SetMigraineAttributes(
                hasAura: command.HasAura,
                auraTypes: command.AuraTypes,
                auraDurationMin: command.AuraDurationMin,
                location: command.Location,
                quality: command.Quality,
                associatedSymptoms: command.AssociatedSymptoms,
                worsenedByActivity: command.WorsenedByActivity,
                functionalImpact: command.FunctionalImpact);
        }

        _db.SymptomEpisodes.Add(episode);
        await _db.SaveChangesAsync(ct);

        // SymptomName is required by EpisodeDto — pull it in a single hop.
        var symptomName = await _db.Symptoms
            .Where(s => s.Id == episode.SymptomId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return new EpisodeDto(
            episode.Id,
            episode.SymptomId,
            symptomName,
            episode.StartedAt,
            episode.EndedAt,
            episode.Severity,
            episode.SeverityAfter,
            episode.IsOnPeriod,
            episode.NoDrugTaken,
            episode.NoDrugReasonCode,
            episode.RetroClosed,
            IntakeCount: 0,
            FirstDrugName: null);
    }
}
