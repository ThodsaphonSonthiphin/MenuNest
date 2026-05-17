using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Health.Intakes.LogNoDrug;

/// <summary>
/// Records that the user chose NOT to take medication for an active
/// episode (with a reason). Still schedules a self-resolving +60 min
/// follow-up so the user is nudged to update their state.
/// </summary>
public sealed record LogNoDrugCommand(
    Guid SymptomEpisodeId,
    NoDrugReason Reason) : ICommand<Unit>;
