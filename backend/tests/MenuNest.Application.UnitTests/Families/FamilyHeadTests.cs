using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Families;

/// <summary>
/// menunest-201: the head is a transferable role that unlocks exactly one
/// power. These pin the transfer rules on the entity itself, where they cannot
/// be bypassed by a handler that forgets to check.
/// </summary>
public class FamilyHeadTests
{
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void A_new_family_has_its_creator_as_head()
    {
        var f = Family.CreateNew("Test", Creator);
        f.HeadUserId.Should().Be(Creator);
    }

    [Fact]
    public void Only_the_current_head_may_transfer_the_role()
    {
        var f = Family.CreateNew("Test", Creator);

        var act = () => f.TransferHeadTo(currentHeadUserId: Other, newHeadUserId: Other);

        act.Should().Throw<DomainException>().WithMessage("*only the family head*");
    }

    [Fact]
    public void The_head_may_hand_the_role_to_another_member()
    {
        var f = Family.CreateNew("Test", Creator);

        f.TransferHeadTo(Creator, Other);

        f.HeadUserId.Should().Be(Other);
    }

    [Fact]
    public void Transferring_to_the_current_head_is_rejected()
    {
        var f = Family.CreateNew("Test", Creator);

        var act = () => f.TransferHeadTo(Creator, Creator);

        act.Should().Throw<DomainException>().WithMessage("*already*");
    }

    [Fact]
    public void A_headless_family_adopts_the_head_it_is_assigned()
    {
        var f = Family.CreateNew("Test", Creator);
        f.ClearHead();
        f.HeadUserId.Should().BeNull();

        f.AssignHead(Other);

        f.HeadUserId.Should().Be(Other);
    }

    [Fact]
    public void Assigning_a_head_to_a_family_that_has_one_is_rejected()
    {
        var f = Family.CreateNew("Test", Creator);

        var act = () => f.AssignHead(Other);

        act.Should().Throw<DomainException>().WithMessage("*already has a head*");
    }
}
