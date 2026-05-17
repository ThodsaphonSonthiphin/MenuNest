using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Health.FollowUps.GetPendingPings;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MenuNest.Infrastructure.BackgroundServices;

/// <summary>
/// Polls <see cref="MenuNest.Domain.Entities.FollowUpPing"/> rows every
/// minute. For each pending ping whose <c>ScheduledAt</c> has passed,
/// it tries to send a web push and marks the ping <c>Asked</c>. Sends
/// fail-quiet: a missing push subscription is not an error (the in-app
/// modal picks up the asked ping on next app open).
///
/// Task 10 wired <see cref="WebPushSender"/> as the real
/// <see cref="IWebPushSender"/> implementation, backed by the
/// <c>WebPush</c> NuGet package and VAPID keys from the <c>Push</c>
/// config section. If VAPID keys are absent the sender logs a warning
/// and returns 0 — the dispatcher stays running regardless.
/// </summary>
public sealed class FollowUpDispatcher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<FollowUpDispatcher> _logger;

    public FollowUpDispatcher(IServiceProvider sp, ILogger<FollowUpDispatcher> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        _logger.LogInformation("FollowUpDispatcher started, tick interval 1 min");

        // First tick fires immediately so the service is responsive on
        // cold start (the PeriodicTimer pattern below waits before
        // every tick, so without this the first scan happens after 1 min).
        while (!ct.IsCancellationRequested)
        {
            await TickAsync(ct);
            try { await timer.WaitForNextTickAsync(ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Single dispatch pass — internal so integration tests can drive it
    /// directly without spinning up the timer loop.
    /// </summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var pushSender = scope.ServiceProvider.GetRequiredService<IWebPushSender>();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var pending = await mediator.Send(new GetPendingPingsQuery(50), ct);
            if (pending.Count == 0)
                return;

            _logger.LogInformation(
                "FollowUpDispatcher tick: {Count} pending pings", pending.Count);

            foreach (var dto in pending)
            {
                var ping = await db.FollowUpPings
                    .FirstOrDefaultAsync(p => p.Id == dto.PingId, ct);
                if (ping is null)
                    continue;

                try
                {
                    // Send push — no-op (returns 0) if user has no
                    // subscriptions. The Null sender used pre-VAPID
                    // (Task 10) always returns 0.
                    var sent = await pushSender.SendFollowUpAsync(ping, ct);

                    // Mark as Asked regardless of send result: the in-app
                    // modal works off Status=Asked and we don't want to
                    // re-send to whichever subscriptions did succeed.
                    if (ping.Status == PingStatus.Pending)
                        ping.MarkAsked();

                    _logger.LogDebug(
                        "Ping {PingId} push sent to {Sent} devices",
                        ping.Id, sent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to send push for ping {PingId}", ping.Id);
                }
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FollowUpDispatcher tick failed");
        }
    }
}
