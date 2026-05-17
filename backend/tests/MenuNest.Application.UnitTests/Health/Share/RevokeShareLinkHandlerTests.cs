using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Share.RevokeShareLink;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.Share;

public class RevokeShareLinkHandlerTests
{
    private static RevokeShareLinkHandler Build(HandlerTestFixture fx)
        => new(fx.Db, fx.UserProvisioner.Object);

    private static async Task<ShareLink> SeedLink(HandlerTestFixture fx, Guid? userIdOverride = null)
    {
        var link = ShareLink.Create(
            userId: userIdOverride ?? fx.User.Id,
            tokenHash: "a".PadRight(64, 'a'),
            dateFrom: new DateOnly(2026, 04, 17),
            dateTo: new DateOnly(2026, 05, 17),
            expiresAt: DateTime.UtcNow.AddDays(30));

        fx.Db.ShareLinks.Add(link);
        await fx.Db.SaveChangesAsync();
        return link;
    }

    [Fact]
    public async Task Sets_revoked_at_on_owned_share_link()
    {
        using var fx = new HandlerTestFixture();
        var link = await SeedLink(fx);

        await Build(fx).Handle(new RevokeShareLinkCommand(link.Id), CancellationToken.None);

        var persisted = fx.Db.ShareLinks.Single(l => l.Id == link.Id);
        persisted.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Is_idempotent_when_already_revoked()
    {
        using var fx = new HandlerTestFixture();
        var link = await SeedLink(fx);

        await Build(fx).Handle(new RevokeShareLinkCommand(link.Id), CancellationToken.None);
        var firstRevoke = fx.Db.ShareLinks.Single(l => l.Id == link.Id).RevokedAt;

        // Second revoke must succeed without changing the timestamp.
        var act = async () => await Build(fx).Handle(
            new RevokeShareLinkCommand(link.Id), CancellationToken.None);
        await act.Should().NotThrowAsync();

        var persisted = fx.Db.ShareLinks.Single(l => l.Id == link.Id);
        persisted.RevokedAt.Should().Be(firstRevoke);
    }

    [Fact]
    public async Task Throws_when_share_link_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var link = await SeedLink(fx, userIdOverride: Guid.NewGuid());

        var act = async () => await Build(fx).Handle(
            new RevokeShareLinkCommand(link.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Throws_when_share_link_not_found()
    {
        using var fx = new HandlerTestFixture();

        var act = async () => await Build(fx).Handle(
            new RevokeShareLinkCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }
}
