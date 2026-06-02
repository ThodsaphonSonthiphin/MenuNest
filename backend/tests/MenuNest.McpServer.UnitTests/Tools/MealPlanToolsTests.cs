using Mediator;
using MenuNest.Application.UseCases.MealPlan;
using MenuNest.Application.UseCases.MealPlan.ListMealPlan;
using MenuNest.Application.UseCases.MealPlan.CreateMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.UpdateMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.DeleteMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.StockCheck;
using MenuNest.Application.UseCases.MealPlan.StockCheckBatch;
using MenuNest.Application.UseCases.MealPlan.CookBatch;
using MenuNest.Domain.Enums;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class MealPlanToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly MealPlanTools _sut;

    public MealPlanToolsTests() => _sut = new MealPlanTools(_mediator.Object);

    [Fact]
    public async Task list_meal_plan_sends_ListMealPlanQuery_with_date_range()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 7);
        _mediator
            .Setup(m => m.Send(It.Is<ListMealPlanQuery>(q => q.From == from && q.To == to), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<MealPlanEntryDto>>(new List<MealPlanEntryDto>()));
        await _sut.list_meal_plan(from, to, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<ListMealPlanQuery>(q => q.From == from && q.To == to), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_meal_plan_entry_sends_CreateMealPlanEntryCommand()
    {
        var date = new DateOnly(2026, 6, 3);
        var recipeId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<CreateMealPlanEntryCommand>(c => c.Date == date && c.RecipeId == recipeId && c.MealSlot == MealSlot.Lunch), It.IsAny<CancellationToken>()))
            .Returns<CreateMealPlanEntryCommand, CancellationToken>((_, _) => new ValueTask<MealPlanEntryDto>((MealPlanEntryDto)default!));
        await _sut.create_meal_plan_entry(date, MealSlot.Lunch, recipeId, null, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CreateMealPlanEntryCommand>(c => c.Date == date && c.RecipeId == recipeId && c.MealSlot == MealSlot.Lunch), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task update_meal_plan_entry_sends_UpdateMealPlanEntryCommand()
    {
        var id = Guid.NewGuid();
        var recipeId = Guid.NewGuid();
        const string notes = "updated notes";
        _mediator
            .Setup(m => m.Send(It.Is<UpdateMealPlanEntryCommand>(c => c.Id == id && c.RecipeId == recipeId && c.Notes == notes), It.IsAny<CancellationToken>()))
            .Returns<UpdateMealPlanEntryCommand, CancellationToken>((_, _) => new ValueTask<MealPlanEntryDto>((MealPlanEntryDto)default!));
        await _sut.update_meal_plan_entry(id, recipeId, notes, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<UpdateMealPlanEntryCommand>(c => c.Id == id && c.RecipeId == recipeId && c.Notes == notes), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_meal_plan_entry_sends_DeleteMealPlanEntryCommand_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteMealPlanEntryCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.delete_meal_plan_entry(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<DeleteMealPlanEntryCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task stock_check_sends_StockCheckQuery_with_entry_id()
    {
        var entryId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<StockCheckQuery>(q => q.MealPlanEntryId == entryId), It.IsAny<CancellationToken>()))
            .Returns<StockCheckQuery, CancellationToken>((_, _) => new ValueTask<StockCheckDto>((StockCheckDto)default!));
        await _sut.stock_check(entryId, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<StockCheckQuery>(q => q.MealPlanEntryId == entryId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task stock_check_batch_sends_StockCheckBatchQuery_with_entry_ids()
    {
        var ids = new Guid[] { Guid.NewGuid(), Guid.NewGuid() };
        _mediator
            .Setup(m => m.Send(It.Is<StockCheckBatchQuery>(q => q.EntryIds.SequenceEqual(ids)), It.IsAny<CancellationToken>()))
            .Returns<StockCheckBatchQuery, CancellationToken>((_, _) => new ValueTask<StockCheckBatchDto>((StockCheckBatchDto)default!));
        await _sut.stock_check_batch(ids, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<StockCheckBatchQuery>(q => q.EntryIds.SequenceEqual(ids)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task cook_batch_sends_CookBatchCommand_with_entry_ids()
    {
        var ids = new Guid[] { Guid.NewGuid(), Guid.NewGuid() };
        _mediator
            .Setup(m => m.Send(It.Is<CookBatchCommand>(c => c.EntryIds.SequenceEqual(ids)), It.IsAny<CancellationToken>()))
            .Returns<CookBatchCommand, CancellationToken>((_, _) => new ValueTask<CookBatchResult>((CookBatchResult)default!));
        await _sut.cook_batch(ids, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CookBatchCommand>(c => c.EntryIds.SequenceEqual(ids)), It.IsAny<CancellationToken>()), Times.Once);
    }
}
