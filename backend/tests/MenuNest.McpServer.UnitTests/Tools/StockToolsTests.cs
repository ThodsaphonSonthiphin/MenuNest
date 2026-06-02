using Mediator;
using MenuNest.Application.UseCases.Stock;
using MenuNest.Application.UseCases.Stock.ListStock;
using MenuNest.Application.UseCases.Stock.UpsertStock;
using MenuNest.Application.UseCases.Stock.DeleteStock;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class StockToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly StockTools _sut;

    public StockToolsTests() => _sut = new StockTools(_mediator.Object);

    [Fact]
    public async Task list_stock_sends_ListStockQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListStockQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<StockItemDto>>(new List<StockItemDto>()));
        await _sut.list_stock(CancellationToken.None);
        _mediator.Verify(m => m.Send(It.IsAny<ListStockQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task upsert_stock_sends_UpsertStockCommand_with_ingredient_and_quantity()
    {
        var ingredientId = Guid.NewGuid();
        const decimal quantity = 250m;
        _mediator
            .Setup(m => m.Send(It.Is<UpsertStockCommand>(c => c.IngredientId == ingredientId && c.Quantity == quantity), It.IsAny<CancellationToken>()))
            .Returns<UpsertStockCommand, CancellationToken>((cmd, _) => new ValueTask<StockItemDto>(
                new StockItemDto(Guid.NewGuid(), cmd.IngredientId, "Flour", "g", cmd.Quantity, DateTime.UtcNow, Guid.NewGuid())));
        await _sut.upsert_stock(ingredientId, quantity, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<UpsertStockCommand>(c => c.IngredientId == ingredientId && c.Quantity == quantity), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_stock_sends_DeleteStockCommand_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteStockCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.delete_stock(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<DeleteStockCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
