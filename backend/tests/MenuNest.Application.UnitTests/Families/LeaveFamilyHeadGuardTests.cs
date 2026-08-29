using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Families.LeaveFamily;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Moq;

namespace MenuNest.Application.UnitTests.Families;

/// <summary>
/// menunest-201: authority is taken deliberately, never handed to somebody
/// automatically. So the head has to pass the role on before leaving — but only
/// while somebody is left to pass it to.
/// </summary>
public class LeaveFamilyHeadGuardTests
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
    public async Task The_head_cannot_leave_while_another_member_remains()
    {
        using var fx = new HandlerTestFixture();   // fx.User created the family, so is head
        AddSecondMember(fx);

        var sut = new LeaveFamilyHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(new LeaveFamilyCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*hand the role over*");
    }

    [Fact]
    public async Task The_head_may_leave_as_the_last_member_and_the_family_becomes_headless()
    {
        using var fx = new HandlerTestFixture();

        await new LeaveFamilyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new LeaveFamilyCommand(), CancellationToken.None);

        fx.Db.Families.Single().HeadUserId.Should().BeNull();
        fx.Db.Users.Single(u => u.Id == fx.User.Id).FamilyId.Should().BeNull();
    }

    [Fact]
    public async Task A_member_who_is_not_the_head_may_leave_freely()
    {
        using var fx = new HandlerTestFixture();
        var other = AddSecondMember(fx);
        fx.UserProvisioner
            .Setup(u => u.RequireFamilyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((other, fx.Family.Id));

        await new LeaveFamilyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new LeaveFamilyCommand(), CancellationToken.None);

        fx.Db.Families.Single().HeadUserId.Should().Be(fx.User.Id);
        fx.Db.Users.Single(u => u.Id == other.Id).FamilyId.Should().BeNull();
    }
}
