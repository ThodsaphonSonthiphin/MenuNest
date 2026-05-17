using System.Text.Json;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.Persistence;
using MenuNest.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MenuNest.Infrastructure.IntegrationTests.Health;

/// <summary>
/// Light-touch coverage for <see cref="WebPushSender"/> — the actual
/// HTTP transport in <c>WebPushClient</c> can't be mocked without
/// real network plumbing, so these tests verify the surrounding
/// branches that fail-safe before any network call:
///   - no subscriptions → returns 0
///   - missing VAPID keys → returns 0 + logs
///   - payload builder structure
///
/// Full E2E is deferred to the Playwright smoke test in Task 19.
/// </summary>
public class WebPushSenderTests
{
    [Fact]
    public async Task SendFollowUpAsync_returns_zero_when_user_has_no_subscriptions()
    {
        await using var sp = BuildServices(withVapidKeys: true);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var (user, symptom, episode, ping) = SeedEpisodeWithPing(db);
        await db.SaveChangesAsync();

        var sut = BuildSender(scope.ServiceProvider, withVapidKeys: true);
        var result = await sut.SendFollowUpAsync(ping, CancellationToken.None);

        result.Should().Be(0, "no subscriptions means nothing to send to");
    }

    [Fact]
    public async Task SendFollowUpAsync_returns_zero_when_vapid_keys_missing()
    {
        await using var sp = BuildServices(withVapidKeys: false);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var (user, symptom, episode, ping) = SeedEpisodeWithPing(db);
        db.WebPushSubscriptions.Add(WebPushSubscription.Create(
            userId: user.Id,
            endpoint: "https://example.com/push/abc",
            p256dh: "BPa0p3SqRf9R8eqmEKBzNH4f7VvFakeKey1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ123",
            auth: "fkJatBBEl-sampleAuthValue"));
        await db.SaveChangesAsync();

        var sut = BuildSender(scope.ServiceProvider, withVapidKeys: false);

        var act = async () => await sut.SendFollowUpAsync(ping, CancellationToken.None);
        await act.Should().NotThrowAsync(
            "missing VAPID keys must fail-safe (log warning) instead of crashing the dispatcher");

        var result = await sut.SendFollowUpAsync(ping, CancellationToken.None);
        result.Should().Be(0);
    }

    [Fact]
    public async Task SendFollowUpAsync_returns_zero_when_ping_episode_missing()
    {
        await using var sp = BuildServices(withVapidKeys: true);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Ping with a fabricated episode id — no episode row exists.
        var orphanPing = FollowUpPing.Schedule(Guid.NewGuid(), DateTime.UtcNow);

        var sut = BuildSender(scope.ServiceProvider, withVapidKeys: true);

        var result = await sut.SendFollowUpAsync(orphanPing, CancellationToken.None);
        result.Should().Be(0);
    }

    [Fact]
    public async Task BuildPayloadAsync_includes_required_fields()
    {
        await using var sp = BuildServices(withVapidKeys: true);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var (user, symptom, episode, ping) = SeedEpisodeWithPing(db);
        await db.SaveChangesAsync();

        var sut = BuildSender(scope.ServiceProvider, withVapidKeys: true);

        var payloadJson = await sut.BuildPayloadAsync(ping, episode, CancellationToken.None);

        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        root.TryGetProperty("title", out var title).Should().BeTrue();
        title.GetString().Should().Contain(symptom.Name);

        root.TryGetProperty("body", out _).Should().BeTrue();

        root.TryGetProperty("data", out var data).Should().BeTrue();
        data.GetProperty("pingId").GetGuid().Should().Be(ping.Id);
        data.GetProperty("episodeId").GetGuid().Should().Be(episode.Id);

        root.TryGetProperty("actions", out var actions).Should().BeTrue();
        actions.ValueKind.Should().Be(JsonValueKind.Array);
        actions.GetArrayLength().Should().Be(4, "four follow-up action buttons (resolved/improved/same/worse)");

        var actionNames = new List<string>();
        foreach (var a in actions.EnumerateArray())
            actionNames.Add(a.GetProperty("action").GetString()!);

        actionNames.Should().Contain(new[] { "resolved", "improved", "same", "worse" });
    }

    [Fact]
    public async Task BuildPayloadAsync_uses_last_drug_name_when_intake_present()
    {
        await using var sp = BuildServices(withVapidKeys: true);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var (user, symptom, episode, ping) = SeedEpisodeWithPing(db);

        var drug = Drug.Create(
            userId: user.Id,
            name: "Paracetamol",
            drugType: DrugType.Analgesic,
            doseStrength: "500mg",
            effectDurationMinHours: 4,
            effectDurationMaxHours: 6,
            maxDailyDose: 8);
        var intake = Intake.Create(
            userId: user.Id,
            drugId: drug.Id,
            doseAmount: 1,
            symptomEpisodeId: episode.Id,
            takenAt: DateTime.UtcNow.AddMinutes(-30));

        db.Drugs.Add(drug);
        db.Intakes.Add(intake);
        await db.SaveChangesAsync();

        var sut = BuildSender(scope.ServiceProvider, withVapidKeys: true);
        var payloadJson = await sut.BuildPayloadAsync(ping, episode, CancellationToken.None);

        using var doc = JsonDocument.Parse(payloadJson);
        var body = doc.RootElement.GetProperty("body").GetString();
        body.Should().Contain("Paracetamol",
            "the body line should reference the most-recent drug taken");
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static ServiceProvider BuildServices(bool withVapidKeys)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseInMemoryDatabase($"webpushsender-tests-{Guid.NewGuid()}"));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        var options = new WebPushOptions
        {
            VapidPublicKey = withVapidKeys
                ? "BDjASz8kkVBQJgWcD05uX3VxIs_gSHyuS023jnBoHBgUbg8zIJvTSQytR8MP4Z3-kzcGNVnMHsZJF6KyXf5gQrk"
                : null,
            VapidPrivateKey = withVapidKeys
                ? "mryM-krWj_6IsIMGsd8wNFXGBxnxKpJpu6jzPyTwYWE"
                : null,
            VapidSubject = "mailto:test@menunest.test",
        };
        services.AddSingleton<IOptions<WebPushOptions>>(Options.Create(options));

        return services.BuildServiceProvider();
    }

    private static WebPushSender BuildSender(IServiceProvider sp, bool withVapidKeys)
    {
        var db = sp.GetRequiredService<IApplicationDbContext>();
        var options = sp.GetRequiredService<IOptions<WebPushOptions>>();
        return new WebPushSender(db, options, NullLogger<WebPushSender>.Instance);
    }

    private static (User user, Symptom symptom, SymptomEpisode episode, FollowUpPing ping)
        SeedEpisodeWithPing(AppDbContext db)
    {
        var user = User.CreateFromExternalLogin(
            externalId: "test-oid",
            email: "test@example.com",
            displayName: "Test User",
            authProvider: AuthProvider.Microsoft);
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(user.Id, symptom.Id, severity: 6);
        var ping = FollowUpPing.Schedule(episode.Id, DateTime.UtcNow.AddMinutes(-1));

        db.Users.Add(user);
        db.Symptoms.Add(symptom);
        db.SymptomEpisodes.Add(episode);
        db.FollowUpPings.Add(ping);

        return (user, symptom, episode, ping);
    }
}
