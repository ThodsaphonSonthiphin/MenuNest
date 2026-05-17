using Mediator;

namespace MenuNest.Application.UseCases.Health.Episodes.ListEpisodes;

/// <summary>
/// History list query — filters by inclusive date range (interpreted as
/// UTC days), symptom, and outcome flags. Pagination is intentionally
/// deferred to Phase 2; the result is ordered <c>StartedAt</c> DESC.
/// </summary>
/// <param name="From">Optional inclusive lower bound (UTC date).</param>
/// <param name="To">Optional inclusive upper bound (UTC date).</param>
/// <param name="SymptomId">Optional symptom filter.</param>
/// <param name="OnlyResolved">True → only episodes that have been resolved
///     via a drug intake (<c>EndedAt IS NOT NULL</c> AND <c>NoDrugTaken = false</c>).</param>
/// <param name="OnlyFailed">True → only "no-drug" episodes
///     (<c>NoDrugTaken = true</c>).</param>
public sealed record ListEpisodesQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? SymptomId = null,
    bool? OnlyResolved = null,
    bool? OnlyFailed = null) : IQuery<IReadOnlyList<EpisodeDto>>;
