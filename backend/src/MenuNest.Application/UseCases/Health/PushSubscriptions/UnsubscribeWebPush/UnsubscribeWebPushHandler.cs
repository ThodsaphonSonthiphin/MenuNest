using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.PushSubscriptions.UnsubscribeWebPush;

public sealed class UnsubscribeWebPushHandler : ICommandHandler<UnsubscribeWebPushCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public UnsubscribeWebPushHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(UnsubscribeWebPushCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Endpoint))
            return Unit.Value;

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);
        var endpoint = command.Endpoint.Trim();

        // Scope by user — never let one user delete another's row even if
        // the endpoint string somehow matched (push services may reuse
        // endpoint shapes across accounts on the same browser profile).
        var existing = await _db.WebPushSubscriptions
            .FirstOrDefaultAsync(
                s => s.UserId == user.Id && s.Endpoint == endpoint,
                ct);

        if (existing is null)
            return Unit.Value;

        _db.WebPushSubscriptions.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
