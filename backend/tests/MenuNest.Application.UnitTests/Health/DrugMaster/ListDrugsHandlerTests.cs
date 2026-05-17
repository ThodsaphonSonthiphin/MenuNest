using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.DrugMaster.ListDrugs;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Health.DrugMaster;

public class ListDrugsHandlerTests
{
    [Fact]
    public async Task Excludes_soft_deleted_drugs()
    {
        using var fx = new HandlerTestFixture();
        var alive = Drug.Create(fx.User.Id, "Alive", DrugType.Analgesic, "500mg", 4, 6, 8);
        var gone = Drug.Create(fx.User.Id, "Gone", DrugType.Analgesic, "500mg", 4, 6, 8);
        gone.SoftDelete();
        fx.Db.Drugs.AddRange(alive, gone);
        await fx.Db.SaveChangesAsync();

        var sut = new ListDrugsHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new ListDrugsQuery(), CancellationToken.None);

        result.Should().ContainSingle(d => d.Name == "Alive");
        result.Should().NotContain(d => d.Name == "Gone");
    }

    [Fact]
    public async Task Filters_by_symptom_when_query_supplies_id()
    {
        using var fx = new HandlerTestFixture();
        var headache = Guid.NewGuid();
        var fever = Guid.NewGuid();
        var para = Drug.Create(fx.User.Id, "Para", DrugType.Analgesic, "500mg", 4, 6, 8,
            treatsSymptomIds: new[] { headache, fever });
        var triptan = Drug.Create(fx.User.Id, "Sumatriptan", DrugType.Triptan, "50mg", 4, 4, 2,
            treatsSymptomIds: new[] { headache });
        var unrelated = Drug.Create(fx.User.Id, "Antacid", DrugType.Other, "10ml", 2, 4, 3);
        fx.Db.Drugs.AddRange(para, triptan, unrelated);
        await fx.Db.SaveChangesAsync();

        var sut = new ListDrugsHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new ListDrugsQuery(SymptomId: headache), CancellationToken.None);

        result.Select(d => d.Name).Should().BeEquivalentTo(new[] { "Para", "Sumatriptan" });
    }

    [Fact]
    public async Task Does_not_leak_other_users_drugs()
    {
        using var fx = new HandlerTestFixture();
        var otherUserId = Guid.NewGuid();
        var mine = Drug.Create(fx.User.Id, "Mine", DrugType.Other, "1", 1, 2, 1);
        var theirs = Drug.Create(otherUserId, "Theirs", DrugType.Other, "1", 1, 2, 1);
        fx.Db.Drugs.AddRange(mine, theirs);
        await fx.Db.SaveChangesAsync();

        var sut = new ListDrugsHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new ListDrugsQuery(), CancellationToken.None);

        result.Should().ContainSingle().Which.Name.Should().Be("Mine");
    }
}
