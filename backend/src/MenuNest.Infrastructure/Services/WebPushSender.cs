using System.Net;
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;

namespace MenuNest.Infrastructure.Services;

/// <summary>
/// Real <see cref="IWebPushSender"/> implementation backed by the
/// <c>WebPush</c> NuGet package. Loads every active
/// <see cref="WebPushSubscription"/> belonging to the ping's episode owner
/// and dispatches a VAPID-signed push to each. Failures are isolated
/// per-subscription so a single bad endpoint cannot derail the
/// <c>FollowUpDispatcher</c> tick.
///
/// HTTP 404 / 410 from the push service means the subscription has been
/// revoked by the browser — those rows are deleted. Other failures only
/// flag the row via <see cref="WebPushSubscription.RecordFailure"/> so we
/// can retry next tick.
/// </summary>
internal sealed class WebPushSender : IWebPushSender
{
    private readonly IApplicationDbContext _db;
    private readonly WebPushOptions _options;
    private readonly ILogger<WebPushSender> _logger;
    private readonly WebPushClient _client;

    public WebPushSender(
        IApplicationDbContext db,
        IOptions<WebPushOptions> options,
        ILogger<WebPushSender> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
        _client = new WebPushClient();
    }

    public async Task<int> SendFollowUpAsync(FollowUpPing ping, CancellationToken ct = default)
    {
        if (!HasValidVapidKeys(_options))
        {
            _logger.LogWarning(
                "WebPushSender: VAPID keys are not configured — skipping push for ping {PingId}. " +
                "Set Push:VapidPublicKey and Push:VapidPrivateKey in configuration.",
                ping.Id);
            return 0;
        }

        // Episode → user → list of devices. Each step bails out cleanly
        // if any link is missing so we never throw on the happy path
        // even when a stray ping references deleted data.
        var episode = await _db.SymptomEpisodes
            .FirstOrDefaultAsync(e => e.Id == ping.SymptomEpisodeId, ct);
        if (episode is null)
        {
            _logger.LogWarning(
                "WebPushSender: ping {PingId} references missing episode {EpisodeId} — skipping",
                ping.Id, ping.SymptomEpisodeId);
            return 0;
        }

        var subscriptions = await _db.WebPushSubscriptions
            .Where(s => s.UserId == episode.UserId)
            .ToListAsync(ct);

        if (subscriptions.Count == 0)
        {
            _logger.LogDebug(
                "WebPushSender: user {UserId} has no push subscriptions — episode {EpisodeId} push skipped",
                episode.UserId, episode.Id);
            return 0;
        }

        var payload = await BuildPayloadAsync(ping, episode, ct);
        var vapid = new VapidDetails(
            _options.VapidSubject,
            _options.VapidPublicKey,
            _options.VapidPrivateKey);

        var successCount = 0;
        var deletions = new List<WebPushSubscription>();

        foreach (var sub in subscriptions)
        {
            try
            {
                var pushSub = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await _client.SendNotificationAsync(pushSub, payload, vapid, ct);
                sub.RecordSuccess();
                successCount++;
            }
            catch (WebPushException ex) when (
                ex.StatusCode == HttpStatusCode.Gone ||
                ex.StatusCode == HttpStatusCode.NotFound)
            {
                // 404 / 410 — the browser revoked this subscription.
                // Removing keeps the table free of dead endpoints.
                _logger.LogInformation(
                    "WebPushSender: removing revoked subscription {SubscriptionId} for user {UserId} (HTTP {Status})",
                    sub.Id, sub.UserId, (int)ex.StatusCode);
                deletions.Add(sub);
            }
            catch (WebPushException ex)
            {
                _logger.LogWarning(ex,
                    "WebPushSender: push failed for subscription {SubscriptionId} (HTTP {Status})",
                    sub.Id, (int)ex.StatusCode);
                sub.RecordFailure();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "WebPushSender: unexpected error sending push for subscription {SubscriptionId}",
                    sub.Id);
                sub.RecordFailure();
            }
        }

        if (deletions.Count > 0)
            _db.WebPushSubscriptions.RemoveRange(deletions);

        await _db.SaveChangesAsync(ct);
        return successCount;
    }

    /// <summary>
    /// Builds the JSON payload the Service Worker receives. Exposed
    /// <c>internal</c> so integration tests can assert structure without
    /// going through the network.
    /// </summary>
    internal async Task<string> BuildPayloadAsync(
        FollowUpPing ping, SymptomEpisode episode, CancellationToken ct)
    {
        var symptomName = await _db.Symptoms
            .Where(s => s.Id == episode.SymptomId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? "อาการ";

        var lastIntake = await _db.Intakes
            .Where(i => i.SymptomEpisodeId == episode.Id)
            .OrderByDescending(i => i.TakenAt)
            .FirstOrDefaultAsync(ct);

        string body;
        if (lastIntake is not null)
        {
            var drugName = await _db.Drugs
                .Where(d => d.Id == lastIntake.DrugId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(ct) ?? "ยา";

            var minutes = (int)Math.Max(0, (DateTime.UtcNow - lastIntake.TakenAt).TotalMinutes);
            body = $"~{minutes} นาทีหลังกิน {drugName}";
        }
        else
        {
            body = symptomName;
        }

        var payload = new
        {
            title = $"🤒 {symptomName} เป็นยังไงบ้าง?",
            body,
            data = new
            {
                pingId = ping.Id,
                episodeId = episode.Id
            },
            actions = new object[]
            {
                new { action = "resolved", title = "หาย" },
                new { action = "improved", title = "ดีขึ้น" },
                new { action = "same",     title = "เท่าเดิม" },
                new { action = "worse",    title = "แย่ลง" }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static bool HasValidVapidKeys(WebPushOptions options)
        => !string.IsNullOrWhiteSpace(options.VapidPublicKey)
        && !string.IsNullOrWhiteSpace(options.VapidPrivateKey);
}
