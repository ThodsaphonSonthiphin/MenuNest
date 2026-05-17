using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.DrugMaster.CreateDrug;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Health.DrugMaster;

public class CreateDrugHandlerTests
{
    [Fact]
    public async Task Creates_drug_with_all_fields_scoped_to_current_user()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateDrugHandler(fx.Db, fx.UserProvisioner.Object, new CreateDrugValidator());

        var treats = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var result = await sut.Handle(
            new CreateDrugCommand(
                Name: "Paracetamol",
                DrugType: DrugType.Analgesic,
                DoseStrength: "500mg",
                EffectDurationMinHours: 4,
                EffectDurationMaxHours: 6,
                MaxDailyDose: 8,
                StockCount: 24,
                ActiveIngredient: "Paracetamol",
                ExpirationDate: new DateOnly(2027, 12, 1),
                UsageNote: "After meals",
                TreatsSymptomIds: treats),
            CancellationToken.None);

        result.Name.Should().Be("Paracetamol");
        result.DoseStrength.Should().Be("500mg");
        result.MaxDailyDose.Should().Be(8);
        result.StockCount.Should().Be(24);
        result.TreatsSymptomIds.Should().BeEquivalentTo(treats);

        var persisted = fx.Db.Drugs.Single(d => d.Id == result.Id);
        persisted.UserId.Should().Be(fx.User.Id);
        persisted.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Create_returns_empty_photos_list_photos_attach_via_separate_endpoint()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateDrugHandler(fx.Db, fx.UserProvisioner.Object, new CreateDrugValidator());

        var result = await sut.Handle(
            new CreateDrugCommand(
                Name: "Ibuprofen",
                DrugType: DrugType.Nsaid,
                DoseStrength: "200mg",
                EffectDurationMinHours: 4,
                EffectDurationMaxHours: 6,
                MaxDailyDose: 6),
            CancellationToken.None);

        result.Photos.Should().BeEmpty();
        fx.Db.Photos.Count(p => p.ParentId == result.Id).Should().Be(0);
    }

    [Fact]
    public async Task Throws_ValidationException_when_name_is_empty()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateDrugHandler(fx.Db, fx.UserProvisioner.Object, new CreateDrugValidator());

        var act = async () => await sut.Handle(
            new CreateDrugCommand("", DrugType.Other, "500mg", 4, 6, 8),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Throws_ValidationException_when_effect_max_is_less_than_min()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateDrugHandler(fx.Db, fx.UserProvisioner.Object, new CreateDrugValidator());

        var act = async () => await sut.Handle(
            new CreateDrugCommand("X", DrugType.Other, "500mg",
                EffectDurationMinHours: 8,
                EffectDurationMaxHours: 4,
                MaxDailyDose: 4),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
