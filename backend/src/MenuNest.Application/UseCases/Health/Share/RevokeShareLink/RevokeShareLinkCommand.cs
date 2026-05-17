using Mediator;

namespace MenuNest.Application.UseCases.Health.Share.RevokeShareLink;

/// <summary>
/// Revokes a share link the current user previously created. Idempotent —
/// revoking a link that is already revoked is a no-op.
/// </summary>
public sealed record RevokeShareLinkCommand(Guid ShareLinkId) : ICommand<Unit>;
