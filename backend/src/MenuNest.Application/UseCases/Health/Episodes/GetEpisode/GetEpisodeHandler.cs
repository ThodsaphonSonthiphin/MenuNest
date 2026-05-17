using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Episodes.GetEpisode;

public sealed class GetEpisodeHandler : IQueryHandler<GetEpisodeQuery, EpisodeDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetEpisodeHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<EpisodeDetailDto> Handle(GetEpisodeQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var episode = await _db.SymptomEpisodes
            .FirstOrDefaultAsync(e => e.Id == query.Id && e.UserId == user.Id, ct)
            ?? throw new DomainException("Episode not found.");

        var symptomName = await _db.Symptoms
            .Where(s => s.Id == episode.SymptomId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        // Intakes joined with the drug for display fields.
        var intakeRows = await (
            from intake in _db.Intakes
            where intake.SymptomEpisodeId == episode.Id
            join drug in _db.Drugs on intake.DrugId equals drug.Id
            orderby intake.TakenAt
            select new EpisodeIntakeDto(
                intake.Id,
                drug.Id,
                drug.Name,
                drug.DoseStrength,
                intake.TakenAt,
                intake.DoseAmount))
            .ToListAsync(ct);

        var followUps = await _db.FollowUpPings
            .Where(p => p.SymptomEpisodeId == episode.Id)
            .OrderBy(p => p.ScheduledAt)
            .Select(p => new EpisodeFollowUpDto(
                p.Id,
                p.ScheduledAt,
                p.AskedAt,
                p.RespondedAt,
                p.Response,
                p.SeverityAtCheck,
                p.Status))
            .ToListAsync(ct);

        var photos = await _db.Photos
            .Where(p => p.ParentType == PhotoParentType.SymptomEpisode
                && p.ParentId == episode.Id
                && p.DeletedAt == null)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PhotoRefDto(p.Id, p.BlobUrl, p.FileSize, p.ContentType))
            .ToListAsync(ct);

        return new EpisodeDetailDto(
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
            episode.Notes,
            episode.RetroClosed,
            episode.RetroEstimatedDuration,
            episode.HasAura,
            episode.AuraDurationMin,
            episode.AuraTypes,
            episode.Location,
            episode.Quality,
            episode.AssociatedSymptoms,
            episode.WorsenedByActivity,
            episode.FunctionalImpact,
            episode.TriggerIds,
            intakeRows,
            followUps,
            photos,
            episode.CreatedAt,
            episode.UpdatedAt);
    }
}
