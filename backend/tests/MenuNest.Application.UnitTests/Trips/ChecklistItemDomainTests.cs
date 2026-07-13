using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class ChecklistItemDomainTests
{
    [Fact]
    public void Create_trims_and_sets_fields()
    {
        var userId = Guid.NewGuid();
        var item = ChecklistItem.Create(userId, "  ร่ม  ");
        item.UserId.Should().Be(userId);
        item.Name.Should().Be("ร่ม");
        item.Id.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string name)
    {
        var act = () => ChecklistItem.Create(Guid.NewGuid(), name);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_name_over_100_chars()
    {
        var act = () => ChecklistItem.Create(Guid.NewGuid(), new string('x', 101));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_empty_userId()
    {
        var act = () => ChecklistItem.Create(Guid.Empty, "ร่ม");
        act.Should().Throw<DomainException>();
    }
}