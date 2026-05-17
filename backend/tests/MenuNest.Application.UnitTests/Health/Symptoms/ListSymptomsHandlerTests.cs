using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Symptoms.ListSymptoms;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Health.Symptoms;

public class ListSymptomsHandlerTests
{
    [Fact]
    public async Task Returns_seeds_and_current_users_customs_but_not_other_users()
    {
        using var fx = new HandlerTestFixture();
        var otherUserId = Guid.NewGuid();

        fx.Db.Symptoms.AddRange(
            Symptom.CreateSeed("ปวดหัว"),
            Symptom.CreateSeed("ไข้"),
            Symptom.CreateCustom("ปวดเอว", fx.User.Id),
            Symptom.CreateCustom("ปวดศอก", fx.User.Id),
            Symptom.CreateCustom("ปวดมือ", otherUserId)); // should NOT appear

        await fx.Db.SaveChangesAsync();
        var sut = new ListSymptomsHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(new ListSymptomsQuery(), CancellationToken.None);

        result.Select(s => s.Name).Should().BeEquivalentTo(
            new[] { "ปวดหัว", "ไข้", "ปวดเอว", "ปวดศอก" });
    }

    [Fact]
    public async Task Sorts_seeds_first_then_customs_by_name()
    {
        using var fx = new HandlerTestFixture();
        fx.Db.Symptoms.AddRange(
            Symptom.CreateCustom("zCustom", fx.User.Id),
            Symptom.CreateSeed("Bseed"),
            Symptom.CreateSeed("Aseed"),
            Symptom.CreateCustom("aCustom", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new ListSymptomsHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new ListSymptomsQuery(), CancellationToken.None);

        result.Select(s => s.Name).Should().ContainInOrder("Aseed", "Bseed", "aCustom", "zCustom");
    }
}
