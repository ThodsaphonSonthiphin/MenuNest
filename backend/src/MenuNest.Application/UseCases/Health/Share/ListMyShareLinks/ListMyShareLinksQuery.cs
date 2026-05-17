using Mediator;

namespace MenuNest.Application.UseCases.Health.Share.ListMyShareLinks;

/// <summary>
/// Returns metadata for every share link the current user has ever
/// created, newest first. Never includes the raw token.
/// </summary>
public sealed record ListMyShareLinksQuery : IQuery<IReadOnlyList<ShareLinkSummaryDto>>;
