using Mediator;

namespace MenuNest.Application.UseCases.Health.Intakes.GetTakeMedicationContext;

/// <summary>
/// Computes the Take Medication picker's 3-category context for an
/// active <see cref="MenuNest.Domain.Entities.SymptomEpisode"/>:
/// <list type="bullet">
///   <item>Active drugs — last intake still within its effect window.</item>
///   <item>Takeable drugs — treat this symptom, in stock, daily cap not reached, not active.</item>
///   <item>Blocked drugs — treat this symptom but currently unavailable (with reason).</item>
/// </list>
/// Drugs that do not treat the episode's symptom are silently dropped.
/// </summary>
public sealed record GetTakeMedicationContextQuery(Guid EpisodeId) : IQuery<TakeMedicationContextDto>;
