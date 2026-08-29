using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Families.ListFamilyMembers;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Families;

/// <summary>
/// menunest-198 wants the head visible to everyone, not only to the head. The
/// SPA decides where the badge goes; this only proves the field is right.
/// </summary>
public class ListFamilyMembersHeadTests
{
    [Fact]
    public async Task Exactly_the_head_is_flagged()
    {
        using var fx = new HandlerTestFixture();   // fx.User created the family, so is head
        var other = User.CreateFromExternalLogin(
            "other-oid", "other@example.com", "Other Member", AuthProvider.Microsoft);
        other.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(other);
        await fx.Db.SaveChangesAsync();

        var members = await new ListFamilyMembersHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new ListFamilyMembersQuery(), CancellationToken.None);

        members.Single(m => m.UserId == fx.User.Id).IsHead.Should().BeTrue();
        members.Single(m => m.UserId == other.Id).IsHead.Should().BeFalse();
        members.Count(m => m.IsHead).Should().Be(1);
    }

    [Fact]
    public async Task A_headless_family_flags_nobody()
    {
        using var fx = new HandlerTestFixture();
        fx.Db.Families.Single().ClearHead();
        await fx.Db.SaveChangesAsync();

        var members = await new ListFamilyMembersHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new ListFamilyMembersQuery(), CancellationToken.None);

        members.Should().OnlyContain(m => !m.IsHead);
    }

    [Fact]
    public async Task Head_and_creator_are_separate_flags()
    {
        using var fx = new HandlerTestFixture();
        var other = User.CreateFromExternalLogin(
            "other-oid", "other@example.com", "Other Member", AuthProvider.Microsoft);
        other.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(other);
        fx.Db.Families.Single().TransferHeadTo(fx.User.Id, other.Id);
        await fx.Db.SaveChangesAsync();

        var members = await new ListFamilyMembersHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new ListFamilyMembersQuery(), CancellationToken.None);

        // The creator flag never moves; the head flag does. That separation is
        // the whole reason menunest-201 did not reuse CreatedByUserId.
        var creator = members.Single(m => m.UserId == fx.User.Id);
        creator.IsCreator.Should().BeTrue();
        creator.IsHead.Should().BeFalse();
        members.Single(m => m.UserId == other.Id).IsHead.Should().BeTrue();
    }
}
