using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.PushSubscriptions.SubscribeWebPush;

/// <summary>
/// Idempotent subscribe: if a subscription with the same endpoint already
/// exists for the current user, returns its id without creating a
/// duplicate. The browser regenerates endpoints on key rotation, so
/// repeat calls from the same device are common and expected.
/// </summary>
public sealed class SubscribeWebPushHandler
    : ICommandHandler<SubscribeWebPushCommand, SubscribeWebPushResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public SubscribeWebPushHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<SubscribeWebPushResultDto> Handle(
        SubscribeWebPushCommand command, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);
        var endpoint = command.Endpoint.Trim();

        var existing = await _db.WebPushSubscriptions
            .FirstOrDefaultAsync(
                s => s.UserId == user.Id && s.Endpoint == endpoint,
                ct);

        if (existing is not null)
            return new SubscribeWebPushResultDto(existing.Id);

        var subscription = WebPushSubscription.Create(
            userId: user.Id,
            endpoint: endpoint,
            p256dh: command.P256dh,
            auth: command.Auth,
            expiresAt: command.ExpiresAt);

        _db.WebPushSubscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);

        return new SubscribeWebPushResultDto(subscription.Id);
    }
}
