using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MenuNest.Infrastructure.Services;

/// <summary>
/// Placeholder <see cref="IWebPushSender"/> used until VAPID / WebPush
/// NuGet is wired in Task 10. Logs and returns 0 (no devices reached)
/// so the <see cref="BackgroundServices.FollowUpDispatcher"/> can run
/// end-to-end in dev without a real push transport.
/// </summary>
internal sealed class NullWebPushSender : IWebPushSender
{
    private readonly ILogger<NullWebPushSender> _logger;

    public NullWebPushSender(ILogger<NullWebPushSender> logger) => _logger = logger;

    public Task<int> SendFollowUpAsync(FollowUpPing ping, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[NULL PUSH] Would notify ping {PingId} for episode {EpisodeId}",
            ping.Id, ping.SymptomEpisodeId);
        return Task.FromResult(0);
    }
}
