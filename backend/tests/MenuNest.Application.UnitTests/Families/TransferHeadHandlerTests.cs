using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Families.TransferHead;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Moq;

namespace MenuNest.Application.UnitTests.Families;

/// <summary>
/// menunest-201: the role moves only when the current head hands it over, and
/// only to somebody who is actually in the family.
/// </summary>
public class TransferHeadHandlerTests
{
    private static User AddSecondMember(HandlerTestFixture fx)
    {
        var other = User.CreateFromExternalLogin(
            externalId: "other-oid", email: "other@example.com",
            displayName: "Other Member", authProvider: AuthProvider.Microsoft);
        other.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(other);
        fx.Db.SaveChanges();
        return other;
    }

    [Fact]
    public async Task The_head_hands_the_role_to_another_member()
    {
        using var fx = new HandlerTestFixture();
        var other = AddSecondMember(fx);

        await new TransferHeadHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new TransferHeadCommand(other.Id), CancellationToken.None);

        fx.Db.Families.Single().HeadUserId.Should().Be(other.Id);
    }

    [Fact]
    public async Task A_member_who_is_not_the_head_cannot_transfer_it()
    {
        using var fx = new HandlerTestFixture();
        var other = AddSecondMember(fx);
        fx.UserProvisioner
            .Setup(u => u.RequireFamilyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((other, fx.Family.Id));

        var act = async () => await new TransferHeadHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new TransferHeadCommand(fx.User.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*only the family head*");
    }

    [Fact]
    public async Task The_role_cannot_be_handed_to_someone_outside_the_family()
    {
        using var fx = new HandlerTestFixture();
        var stranger = User.CreateFromExternalLogin(
            "stranger-oid", "stranger@example.com", "Stranger", AuthProvider.Microsoft);
        fx.Db.Users.Add(stranger);
        await fx.Db.SaveChangesAsync();

        var act = async () => await new TransferHeadHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new TransferHeadCommand(stranger.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*not a member*");
    }
}
