using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.MealPlan.StockCheckBatch;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.MealPlan;

public class StockCheckBatchHandlerTests
{
    [Fact]
    public async Task Aggregates_required_quantities_across_entries()
    {
        using var fx = new HandlerTestFixture();

        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        var rice = Ingredient.Create(fx.Family.Id, "ข้าวสาร", "ถ้วย");
        fx.Db.Ingredients.AddRange(egg, rice);

        var omelet = Recipe.Create(fx.Family.Id, "ไข่เจียว", fx.User.Id);
        omelet.AddIngredient(egg.Id, 2m);
        var congee = Recipe.Create(fx.Family.Id, "โจ๊ก", fx.User.Id);
        congee.AddIngredient(egg.Id, 1m);
        congee.AddIngredient(rice.Id, 2m);
        fx.Db.Recipes.AddRange(omelet, congee);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, 5m, fx.User.Id));
        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, rice.Id, 1m, fx.User.Id));

        var date = new DateOnly(2026, 4, 13);
        var e1 = MealPlanEntry.Create(fx.Family.Id, date, MealSlot.Breakfast, omelet.Id, fx.User.Id);
        var e2 = MealPlanEntry.Create(fx.Family.Id, date, MealSlot.Breakfast, congee.Id, fx.User.Id);
        fx.Db.MealPlanEntries.AddRange(e1, e2);
        await fx.Db.SaveChangesAsync();

        var sut = new StockCheckBatchHandler(fx.Db, fx.UserProvisioner.Object, new StockCheckBatchValidator());
        var result = await sut.Handle(new StockCheckBatchQuery(new[] { e1.Id, e2.Id }), CancellationToken.None);

        result.Lines.Should().HaveCount(2);
        var eggLine = result.Lines.Single(l => l.IngredientId == egg.Id);
        eggLine.Required.Should().Be(3m);
        eggLine.Available.Should().Be(5m);
        eggLine.Missing.Should().Be(0m);

        var riceLine = result.Lines.Single(l => l.IngredientId == rice.Id);
        riceLine.Required.Should().Be(2m);
        riceLine.Available.Should().Be(1m);
        riceLine.Missing.Should().Be(1m);

        result.IsSufficient.Should().BeFalse();
        result.MissingCount.Should().Be(1);
    }

    [Fact]
    public async Task Empty_entry_list_returns_empty_result()
    {
        using var fx = new HandlerTestFixture();
        var sut = new StockCheckBatchHandler(fx.Db, fx.UserProvisioner.Object, new StockCheckBatchValidator());

        var result = await sut.Handle(new StockCheckBatchQuery(Array.Empty<Guid>()), CancellationToken.None);

        result.Lines.Should().BeEmpty();
        result.IsSufficient.Should().BeTrue();
        result.MissingCount.Should().Be(0);
    }

    [Fact]
    public async Task Throws_when_an_entry_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();
        var otherFamily = Family.CreateNew("Other", Guid.NewGuid());
        fx.Db.Families.Add(otherFamily);
        var stranger = MealPlanEntry.Create(
            otherFamily.Id, new DateOnly(2026, 4, 13), MealSlot.Breakfast, Guid.NewGuid(), Guid.NewGuid());
        fx.Db.MealPlanEntries.Add(stranger);
        await fx.Db.SaveChangesAsync();

        var sut = new StockCheckBatchHandler(fx.Db, fx.UserProvisioner.Object, new StockCheckBatchValidator());

        Func<Task> act = () => sut.Handle(new StockCheckBatchQuery(new[] { stranger.Id }), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public void Validator_rejects_empty_guid_in_list()
    {
        var validator = new StockCheckBatchValidator();

        var result = validator.Validate(
            new StockCheckBatchQuery(new[] { Guid.NewGuid(), Guid.Empty }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.StartsWith("EntryIds"));
    }
}
