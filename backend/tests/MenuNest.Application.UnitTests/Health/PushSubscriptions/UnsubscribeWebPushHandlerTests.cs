using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.PushSubscriptions.UnsubscribeWebPush;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Health.PushSubscriptions;

public class UnsubscribeWebPushHandlerTests
{
    private const string SampleEndpoint = "https://fcm.googleapis.com/fcm/send/sample-endpoint";
    private const string SampleP256dh = "BPa0p3SqRf9R8eqmEKBzNH4f7VvFakeKey1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ123";
    private const string SampleAuth = "fkJatBBEl-sampleAuthValue";

    [Fact]
    public async Task Removes_matching_subscription_for_current_user()
    {
        using var fx = new HandlerTestFixture();
        var sub = WebPushSubscription.Create(
            fx.User.Id, SampleEndpoint, SampleP256dh, SampleAuth);
        fx.Db.WebPushSubscriptions.Add(sub);
        await fx.Db.SaveChangesAsync();

        var sut = new UnsubscribeWebPushHandler(fx.Db, fx.UserProvisioner.Object);

        await sut.Handle(
            new UnsubscribeWebPushCommand(SampleEndpoint),
            CancellationToken.None);

        fx.Db.WebPushSubscriptions.Any(s => s.Id == sub.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Is_idempotent_when_endpoint_not_found()
    {
        using var fx = new HandlerTestFixture();
        var sut = new UnsubscribeWebPushHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(
            new UnsubscribeWebPushCommand("https://example.com/push/missing"),
            CancellationToken.None);

        await act.Should().NotThrowAsync(
            "unsubscribing a non-existent endpoint must succeed silently");
    }

    [Fact]
    public async Task Does_not_delete_other_users_subscription_with_same_endpoint()
    {
        using var fx = new HandlerTestFixture();
        var otherUserId = Guid.NewGuid();
        var otherSub = WebPushSubscription.Create(
            otherUserId, SampleEndpoint, SampleP256dh, SampleAuth);
        fx.Db.WebPushSubscriptions.Add(otherSub);
        await fx.Db.SaveChangesAsync();

        var sut = new UnsubscribeWebPushHandler(fx.Db, fx.UserProvisioner.Object);

        await sut.Handle(
            new UnsubscribeWebPushCommand(SampleEndpoint),
            CancellationToken.None);

        fx.Db.WebPushSubscriptions.Any(s => s.Id == otherSub.Id).Should().BeTrue(
            "another user's subscription with the same endpoint must remain");
    }

    [Fact]
    public async Task Trims_endpoint_before_matching()
    {
        using var fx = new HandlerTestFixture();
        var sub = WebPushSubscription.Create(
            fx.User.Id, SampleEndpoint, SampleP256dh, SampleAuth);
        fx.Db.WebPushSubscriptions.Add(sub);
        await fx.Db.SaveChangesAsync();

        var sut = new UnsubscribeWebPushHandler(fx.Db, fx.UserProvisioner.Object);

        await sut.Handle(
            new UnsubscribeWebPushCommand($"  {SampleEndpoint}  "),
            CancellationToken.None);

        fx.Db.WebPushSubscriptions.Any(s => s.Id == sub.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Empty_endpoint_is_noop()
    {
        using var fx = new HandlerTestFixture();
        var sub = WebPushSubscription.Create(
            fx.User.Id, SampleEndpoint, SampleP256dh, SampleAuth);
        fx.Db.WebPushSubscriptions.Add(sub);
        await fx.Db.SaveChangesAsync();

        var sut = new UnsubscribeWebPushHandler(fx.Db, fx.UserProvisioner.Object);

        await sut.Handle(
            new UnsubscribeWebPushCommand(string.Empty),
            CancellationToken.None);

        fx.Db.WebPushSubscriptions.Any(s => s.Id == sub.Id).Should().BeTrue(
            "an empty endpoint must not blindly delete subscriptions");
    }
}
