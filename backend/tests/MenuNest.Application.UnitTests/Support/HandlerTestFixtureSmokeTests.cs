using FluentAssertions;

namespace MenuNest.Application.UnitTests.Support;

public class HandlerTestFixtureSmokeTests
{
    [Fact]
    public void Fixture_seeds_a_user_in_a_family()
    {
        using var fx = new HandlerTestFixture();

        fx.User.FamilyId.Should().Be(fx.Family.Id);
        fx.Db.Users.Should().ContainSingle();
        fx.Db.Families.Should().ContainSingle();
    }
}
