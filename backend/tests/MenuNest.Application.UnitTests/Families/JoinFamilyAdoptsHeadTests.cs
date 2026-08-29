using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Families.JoinFamily;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Moq;

namespace MenuNest.Application.UnitTests.Families;

/// <summary>
/// menunest-201 rule 4. Without this a family that went headless could never
/// regain a head, and no member could ever undo another's change again.
/// </summary>
public class JoinFamilyAdoptsHeadTests
{
    private static (HandlerTestFixture fx, User joiner) Arrange(bool headless)
    {
        var fx = new HandlerTestFixture();
        if (headless)
        {
            fx.Db.Families.Single().ClearHead();
        }

        var joiner = User.CreateFromExternalLogin(
            externalId: "joiner-oid", email: "joiner@example.com",
            displayName: "Joiner", authProvider: AuthProvider.Microsoft);
        fx.Db.Users.Add(joiner);
        fx.Db.SaveChanges();

        fx.UserProvisioner
            .Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(joiner);
        return (fx, joiner);
    }

    [Fact]
    public async Task A_headless_family_makes_its_next_joiner_the_head()
    {
        var (fx, joiner) = Arrange(headless: true);
        using var _ = fx;
        var code = fx.Db.Families.Single().InviteCode.Value;

        await new JoinFamilyHandler(fx.Db, fx.UserProvisioner.Object, new JoinFamilyValidator())
            .Handle(new JoinFamilyCommand(code), CancellationToken.None);

        fx.Db.Families.Single().HeadUserId.Should().Be(joiner.Id);
    }

    [Fact]
    public async Task A_family_that_has_a_head_keeps_it_when_someone_joins()
    {
        var (fx, joiner) = Arrange(headless: false);
        using var _ = fx;
        var code = fx.Db.Families.Single().InviteCode.Value;

        await new JoinFamilyHandler(fx.Db, fx.UserProvisioner.Object, new JoinFamilyValidator())
            .Handle(new JoinFamilyCommand(code), CancellationToken.None);

        fx.Db.Families.Single().HeadUserId.Should().Be(fx.User.Id);
        fx.Db.Families.Single().HeadUserId.Should().NotBe(joiner.Id);
    }
}
