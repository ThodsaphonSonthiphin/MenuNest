using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Episodes.UpdateEpisode;

public sealed class UpdateEpisodeHandler : ICommandHandler<UpdateEpisodeCommand, EpisodeDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<UpdateEpisodeCommand> _validator;

    public UpdateEpisodeHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<UpdateEpisodeCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<EpisodeDetailDto> Handle(UpdateEpisodeCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var episode = await _db.SymptomEpisodes
            .FirstOrDefaultAsync(e => e.Id == command.Id && e.UserId == user.Id, ct)
            ?? throw new DomainException("Episode not found.");

        if (command.Severity.HasValue)
            episode.UpdateSeverity(command.Severity.Value);
        if (command.Notes is not null)
            episode.UpdateNotes(command.Notes);
        if (command.IsOnPeriod.HasValue)
            episode.SetIsOnPeriod(command.IsOnPeriod.Value);
        if (command.TriggerIds is not null)
            episode.SetTriggers(command.TriggerIds);

        if (command.MigraineAttributesProvided)
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

        await _db.SaveChangesAsync(ct);

        return await BuildDetailAsync(episode.Id, ct);
    }

    private async Task<EpisodeDetailDto> BuildDetailAsync(Guid episodeId, CancellationToken ct)
    {
        var episode = await _db.SymptomEpisodes.FirstAsync(e => e.Id == episodeId, ct);

        var symptomName = await _db.Symptoms
            .Where(s => s.Id == episode.SymptomId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var intakeRows = await (
            from intake in _db.Intakes
            where intake.SymptomEpisodeId == episode.Id
            join drug in _db.Drugs on intake.DrugId equals drug.Id
            orderby intake.TakenAt
            select new EpisodeIntakeDto(
                intake.Id, drug.Id, drug.Name, drug.DoseStrength,
                intake.TakenAt, intake.DoseAmount))
            .ToListAsync(ct);

        var followUps = await _db.FollowUpPings
            .Where(p => p.SymptomEpisodeId == episode.Id)
            .OrderBy(p => p.ScheduledAt)
            .Select(p => new EpisodeFollowUpDto(
                p.Id, p.ScheduledAt, p.AskedAt, p.RespondedAt,
                p.Response, p.SeverityAtCheck, p.Status))
            .ToListAsync(ct);

        var photos = await _db.Photos
            .Where(p => p.ParentType == PhotoParentType.SymptomEpisode
                && p.ParentId == episode.Id && p.DeletedAt == null)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PhotoRefDto(p.Id, p.BlobUrl, p.FileSize, p.ContentType))
            .ToListAsync(ct);

        return new EpisodeDetailDto(
            episode.Id, episode.SymptomId, symptomName,
            episode.StartedAt, episode.EndedAt, episode.Severity, episode.SeverityAfter,
            episode.IsOnPeriod, episode.NoDrugTaken, episode.NoDrugReasonCode,
            episode.Notes, episode.RetroClosed, episode.RetroEstimatedDuration,
            episode.HasAura, episode.AuraDurationMin, episode.AuraTypes,
            episode.Location, episode.Quality, episode.AssociatedSymptoms,
            episode.WorsenedByActivity, episode.FunctionalImpact,
            episode.TriggerIds,
            intakeRows, followUps, photos,
            episode.CreatedAt, episode.UpdatedAt);
    }
}
