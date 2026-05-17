using Mediator;

namespace MenuNest.Application.UseCases.Health.Intakes.LogIntake;

/// <summary>
/// Records a single medication intake. When linked to an active episode
/// the handler also schedules a +30 min follow-up ping and marks any
/// prior pending ping for that episode as missed.
/// </summary>
public sealed record LogIntakeCommand(
    Guid DrugId,
    int DoseAmount,
    Guid? SymptomEpisodeId = null,
    DateTime? TakenAt = null,
    string? Notes = null) : ICommand<IntakeDto>;
