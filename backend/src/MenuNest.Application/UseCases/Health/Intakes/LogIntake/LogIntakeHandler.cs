using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Intakes.LogIntake;

public sealed class LogIntakeHandler : ICommandHandler<LogIntakeCommand, IntakeDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<LogIntakeCommand> _validator;
    private readonly IClock _clock;

    public LogIntakeHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<LogIntakeCommand> validator,
        IClock clock)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
        _clock = clock;
    }

    public async ValueTask<IntakeDto> Handle(LogIntakeCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var drug = await _db.Drugs
            .FirstOrDefaultAsync(d => d.Id == command.DrugId
                && d.UserId == user.Id
                && d.DeletedAt == null, ct)
            ?? throw new DomainException("Drug not found.");

        // Authorise the episode link before creating the intake — fails
        // fast and avoids orphan intakes pointing at someone else's episode.
        if (command.SymptomEpisodeId.HasValue)
        {
            var episodeExists = await _db.SymptomEpisodes
                .AnyAsync(e => e.Id == command.SymptomEpisodeId.Value
                    && e.UserId == user.Id, ct);
            if (!episodeExists)
                throw new DomainException("Episode not found.");
        }

        var intake = Intake.Create(
            userId: user.Id,
            drugId: drug.Id,
            doseAmount: command.DoseAmount,
            symptomEpisodeId: command.SymptomEpisodeId,
            takenAt: command.TakenAt,
            notes: command.Notes);

        _db.Intakes.Add(intake);

        // When linked to an active episode, mark any still-pending ping
        // for that episode as missed (only one active ping per episode)
        // and schedule a fresh +30 min follow-up.
        if (command.SymptomEpisodeId.HasValue)
        {
            var pendingPings = await _db.FollowUpPings
                .Where(p => p.SymptomEpisodeId == command.SymptomEpisodeId.Value
                    && p.Status == PingStatus.Pending)
                .ToListAsync(ct);
            foreach (var p in pendingPings)
                p.MarkMissed();

            var ping = FollowUpPing.Schedule(
                command.SymptomEpisodeId.Value,
                _clock.UtcNow.AddMinutes(30));
            _db.FollowUpPings.Add(ping);
        }

        await _db.SaveChangesAsync(ct);

        return new IntakeDto(
            Id: intake.Id,
            DrugId: drug.Id,
            DrugName: drug.Name,
            SymptomEpisodeId: intake.SymptomEpisodeId,
            TakenAt: intake.TakenAt,
            DoseAmount: intake.DoseAmount);
    }
}
