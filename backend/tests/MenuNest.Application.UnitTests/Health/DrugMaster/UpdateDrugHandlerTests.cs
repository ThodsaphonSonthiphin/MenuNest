using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.DrugMaster.UpdateDrug;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.DrugMaster;

public class UpdateDrugHandlerTests
{
    [Fact]
    public async Task Updates_profile_stock_and_treats_in_one_call()
    {
        using var fx = new HandlerTestFixture();
        var drug = Drug.Create(fx.User.Id, "Old", DrugType.Other, "100mg", 2, 4, 4, stockCount: 5);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var newTreats = new[] { Guid.NewGuid() };
        var sut = new UpdateDrugHandler(fx.Db, fx.UserProvisioner.Object, new UpdateDrugValidator());

        var result = await sut.Handle(
            new UpdateDrugCommand(
                Id: drug.Id,
                Name: "New",
                DrugType: DrugType.Triptan,
                DoseStrength: "50mg",
                EffectDurationMinHours: 4,
                EffectDurationMaxHours: 4,
                MaxDailyDose: 2,
                StockCount: 12,
                ActiveIngredient: "Sumatriptan",
                ExpirationDate: new DateOnly(2028, 3, 1),
                UsageNote: "At onset of attack",
                TreatsSymptomIds: newTreats),
            CancellationToken.None);

        result.Name.Should().Be("New");
        result.DrugType.Should().Be(DrugType.Triptan);
        result.StockCount.Should().Be(12);
        result.TreatsSymptomIds.Should().BeEquivalentTo(newTreats);
        result.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Throws_when_drug_not_found()
    {
        using var fx = new HandlerTestFixture();
        var sut = new UpdateDrugHandler(fx.Db, fx.UserProvisioner.Object, new UpdateDrugValidator());

        var act = async () => await sut.Handle(
            new UpdateDrugCommand(
                Id: Guid.NewGuid(),
                Name: "X", DrugType: DrugType.Other, DoseStrength: "1",
                EffectDurationMinHours: 1, EffectDurationMaxHours: 2, MaxDailyDose: 1,
                StockCount: 0,
                ActiveIngredient: null, ExpirationDate: null, UsageNote: null,
                TreatsSymptomIds: null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Drug not found.");
    }
}
