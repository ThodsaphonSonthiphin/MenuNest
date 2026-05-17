using FluentAssertions;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Health;
using MenuNest.Application.UseCases.Health.FollowUps.GetPendingPings;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.BackgroundServices;
using MenuNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MenuNest.Infrastructure.IntegrationTests.Health;

/// <summary>
/// Verifies that <see cref="FollowUpDispatcher"/> reads due pings via
/// <c>IMediator</c>, sends pushes via <c>IWebPushSender</c>, marks the
/// pings <c>Asked</c>, and persists the state. The mediator is mocked
/// so this test focuses on the dispatcher loop — the
/// <c>GetPendingPingsHandler</c> itself has its own unit tests.
/// </summary>
public class FollowUpDispatcherTests
{
    private static readonly DateTime FixedNow =
        new(2026, 05, 17, 12, 00, 00, DateTimeKind.Utc);

    [Fact]
    public async Task TickAsync_marks_due_ping_as_asked_and_invokes_push_sender()
    {
        var (services, mediator, pushSender) = BuildServices();
        await using var rootScope = services.CreateAsyncScope();
        var db = rootScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Seed three pings: past-due-pending, future-due-pending, answered.
        var user = User.CreateFromExternalLogin(
            externalId: "test-oid", email: "test@example.com",
            displayName: "Test", authProvider: AuthProvider.Microsoft);
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(user.Id, symptom.Id, severity: 6);

        var duePing = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-1));
        var futurePing = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(15));
        var answeredPing = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-30));
        answeredPing.MarkAsked();
        answeredPing.RecordResponse(PingResponse.Same);

        db.Users.Add(user);
        db.Symptoms.Add(symptom);
        db.SymptomEpisodes.Add(episode);
        db.FollowUpPings.AddRange(duePing, futurePing, answeredPing);
        await db.SaveChangesAsync();

        // Mediator returns only the due ping — mirrors what
        // GetPendingPingsHandler would return for this state.
        SetupPendingPings(mediator, new[]
        {
            BuildDto(duePing.Id, episode, user.Id, symptom),
        });

        // Act
        var dispatcher = new FollowUpDispatcher(
            services, NullLogger<FollowUpDispatcher>.Instance);
        await dispatcher.TickAsync(CancellationToken.None);

        // Assert: push sender was invoked for the due ping, once.
        pushSender.Verify(
            p => p.SendFollowUpAsync(
                It.Is<FollowUpPing>(fp => fp.Id == duePing.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
        pushSender.Verify(
            p => p.SendFollowUpAsync(
                It.IsAny<FollowUpPing>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Persistence: re-read from a fresh scope.
        await using var checkScope = services.CreateAsyncScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var persistedDue = await checkDb.FollowUpPings.FirstAsync(p => p.Id == duePing.Id);
        persistedDue.Status.Should().Be(PingStatus.Asked,
            "the dispatcher must flip Pending → Asked after invoking the push sender");
        persistedDue.AskedAt.Should().NotBeNull();

        var persistedFuture = await checkDb.FollowUpPings.FirstAsync(p => p.Id == futurePing.Id);
        persistedFuture.Status.Should().Be(PingStatus.Pending,
            "future-due pings must not be touched by this tick");

        var persistedAnswered = await checkDb.FollowUpPings.FirstAsync(p => p.Id == answeredPing.Id);
        persistedAnswered.Status.Should().Be(PingStatus.Answered,
            "already-answered pings must remain Answered");
    }

    [Fact]
    public async Task TickAsync_is_noop_when_mediator_returns_no_pings()
    {
        var (services, mediator, pushSender) = BuildServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        SetupPendingPings(mediator, Array.Empty<PendingPingDto>());

        var dispatcher = new FollowUpDispatcher(
            services, NullLogger<FollowUpDispatcher>.Instance);
        await dispatcher.TickAsync(CancellationToken.None);

        pushSender.Verify(
            p => p.SendFollowUpAsync(
                It.IsAny<FollowUpPing>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TickAsync_swallows_per_ping_push_failures_and_continues()
    {
        var (services, mediator, pushSender) = BuildServices();

        // First push throws, second succeeds. The second must still be
        // marked Asked.
        pushSender.SetupSequence(p => p.SendFollowUpAsync(
            It.IsAny<FollowUpPing>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated push failure"))
            .ReturnsAsync(0);

        await using var rootScope = services.CreateAsyncScope();
        var db = rootScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var user = User.CreateFromExternalLogin(
            externalId: "test-oid", email: "test@example.com",
            displayName: "Test", authProvider: AuthProvider.Microsoft);
        var symptom = Symptom.CreateSeed("ปวดหัว");
        var episode = SymptomEpisode.Start(user.Id, symptom.Id, severity: 5);
        var firstPing = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-5));
        var secondPing = FollowUpPing.Schedule(episode.Id, FixedNow.AddMinutes(-2));

        db.Users.Add(user);
        db.Symptoms.Add(symptom);
        db.SymptomEpisodes.Add(episode);
        db.FollowUpPings.AddRange(firstPing, secondPing);
        await db.SaveChangesAsync();

        SetupPendingPings(mediator, new[]
        {
            BuildDto(firstPing.Id, episode, user.Id, symptom),
            BuildDto(secondPing.Id, episode, user.Id, symptom),
        });

        var dispatcher = new FollowUpDispatcher(
            services, NullLogger<FollowUpDispatcher>.Instance);
        await dispatcher.TickAsync(CancellationToken.None);

        await using var checkScope = services.CreateAsyncScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // First ping: push threw before MarkAsked → stays Pending.
        var persistedFirst = await checkDb.FollowUpPings.FirstAsync(p => p.Id == firstPing.Id);
        persistedFirst.Status.Should().Be(PingStatus.Pending);

        // Second ping: push succeeded → marked Asked.
        var persistedSecond = await checkDb.FollowUpPings.FirstAsync(p => p.Id == secondPing.Id);
        persistedSecond.Status.Should().Be(PingStatus.Asked);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static (ServiceProvider sp, Mock<IMediator> mediator, Mock<IWebPushSender> pushSender)
        BuildServices()
    {
        var services = new ServiceCollection();

        var dbName = $"dispatcher-tests-{Guid.NewGuid()}";
        services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IClock>(new FixedClock(FixedNow));

        var pushMock = new Mock<IWebPushSender>();
        // Default: no-op push (returns 0). Per-test SetupSequence can
        // override this in BuildServices's local mock.
        pushMock.Setup(p => p.SendFollowUpAsync(
            It.IsAny<FollowUpPing>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        services.AddScoped(_ => pushMock.Object);

        var mediatorMock = new Mock<IMediator>();
        services.AddScoped(_ => mediatorMock.Object);

        services.AddLogging();

        return (services.BuildServiceProvider(), mediatorMock, pushMock);
    }

    private static void SetupPendingPings(
        Mock<IMediator> mediator, IReadOnlyList<PendingPingDto> dtos)
    {
        mediator.Setup(m => m.Send(
                It.IsAny<GetPendingPingsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<PendingPingDto>>(dtos));
    }

    private static PendingPingDto BuildDto(
        Guid pingId, SymptomEpisode episode, Guid userId, Symptom symptom)
        => new(
            PingId: pingId,
            EpisodeId: episode.Id,
            UserId: userId,
            SymptomId: symptom.Id,
            SymptomName: symptom.Name,
            ScheduledAt: FixedNow,
            Severity: episode.Severity,
            LastIntakeAt: null,
            LastDrugName: null,
            MinutesSinceLastIntake: 0);

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; }
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
    }
}
