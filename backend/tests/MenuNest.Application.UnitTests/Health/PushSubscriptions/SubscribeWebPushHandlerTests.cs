using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.PushSubscriptions.SubscribeWebPush;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Health.PushSubscriptions;

public class SubscribeWebPushHandlerTests
{
    private const string SampleEndpoint = "https://fcm.googleapis.com/fcm/send/sample-endpoint";
    private const string SampleP256dh = "BPa0p3SqRf9R8eqmEKBzNH4f7VvFakeKey1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ123";
    private const string SampleAuth = "fkJatBBEl-sampleAuthValue";

    [Fact]
    public async Task Creates_subscription_scoped_to_current_user()
    {
        using var fx = new HandlerTestFixture();
        var sut = new SubscribeWebPushHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new SubscribeWebPushCommand(SampleEndpoint, SampleP256dh, SampleAuth, ExpiresAt: null),
            CancellationToken.None);

        result.Id.Should().NotBe(Guid.Empty);

        var persisted = fx.Db.WebPushSubscriptions.Single(s => s.Id == result.Id);
        persisted.UserId.Should().Be(fx.User.Id);
        persisted.Endpoint.Should().Be(SampleEndpoint);
        persisted.P256dh.Should().Be(SampleP256dh);
        persisted.Auth.Should().Be(SampleAuth);
    }

    [Fact]
    public async Task Is_idempotent_returning_same_id_for_repeat_endpoint()
    {
        using var fx = new HandlerTestFixture();
        var sut = new SubscribeWebPushHandler(fx.Db, fx.UserProvisioner.Object);

        var first = await sut.Handle(
            new SubscribeWebPushCommand(SampleEndpoint, SampleP256dh, SampleAuth, ExpiresAt: null),
            CancellationToken.None);
        var second = await sut.Handle(
            new SubscribeWebPushCommand(SampleEndpoint, SampleP256dh, SampleAuth, ExpiresAt: null),
            CancellationToken.None);

        second.Id.Should().Be(first.Id);
        fx.Db.WebPushSubscriptions.Count().Should().Be(1,
            "the same endpoint must not produce a duplicate row");
    }

    [Fact]
    public async Task Trims_endpoint_when_matching_existing_row()
    {
        using var fx = new HandlerTestFixture();
        var sut = new SubscribeWebPushHandler(fx.Db, fx.UserProvisioner.Object);

        var first = await sut.Handle(
            new SubscribeWebPushCommand(SampleEndpoint, SampleP256dh, SampleAuth, ExpiresAt: null),
            CancellationToken.None);
        var second = await sut.Handle(
            new SubscribeWebPushCommand($"  {SampleEndpoint}  ", SampleP256dh, SampleAuth, ExpiresAt: null),
            CancellationToken.None);

        second.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task Different_users_can_subscribe_with_same_endpoint()
    {
        using var fx = new HandlerTestFixture();

        // Pre-seed another user's subscription with the same endpoint.
        var otherUserSub = WebPushSubscription.Create(
            userId: Guid.NewGuid(),
            endpoint: SampleEndpoint,
            p256dh: SampleP256dh,
            auth: SampleAuth);
        fx.Db.WebPushSubscriptions.Add(otherUserSub);
        await fx.Db.SaveChangesAsync();

        var sut = new SubscribeWebPushHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new SubscribeWebPushCommand(SampleEndpoint, SampleP256dh, SampleAuth, ExpiresAt: null),
            CancellationToken.None);

        result.Id.Should().NotBe(otherUserSub.Id,
            "the current user gets a brand-new row even if another user owns the same endpoint string");

        fx.Db.WebPushSubscriptions.Count().Should().Be(2);
    }

    [Fact]
    public void Validator_rejects_empty_endpoint()
    {
        var validator = new SubscribeWebPushValidator();
        var result = validator.Validate(new SubscribeWebPushCommand(
            Endpoint: string.Empty,
            P256dh: SampleP256dh,
            Auth: SampleAuth,
            ExpiresAt: null));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_empty_p256dh()
    {
        var validator = new SubscribeWebPushValidator();
        var result = validator.Validate(new SubscribeWebPushCommand(
            Endpoint: SampleEndpoint,
            P256dh: string.Empty,
            Auth: SampleAuth,
            ExpiresAt: null));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_empty_auth()
    {
        var validator = new SubscribeWebPushValidator();
        var result = validator.Validate(new SubscribeWebPushCommand(
            Endpoint: SampleEndpoint,
            P256dh: SampleP256dh,
            Auth: string.Empty,
            ExpiresAt: null));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_passes_for_all_fields_set()
    {
        var validator = new SubscribeWebPushValidator();
        var result = validator.Validate(new SubscribeWebPushCommand(
            Endpoint: SampleEndpoint,
            P256dh: SampleP256dh,
            Auth: SampleAuth,
            ExpiresAt: null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Persists_expires_at_when_provided()
    {
        using var fx = new HandlerTestFixture();
        var sut = new SubscribeWebPushHandler(fx.Db, fx.UserProvisioner.Object);
        var expiry = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await sut.Handle(
            new SubscribeWebPushCommand(SampleEndpoint, SampleP256dh, SampleAuth, ExpiresAt: expiry),
            CancellationToken.None);

        var persisted = fx.Db.WebPushSubscriptions.Single(s => s.Id == result.Id);
        persisted.ExpiresAt.Should().Be(expiry);
    }
}
