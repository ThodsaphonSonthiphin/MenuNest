using Mediator;
using MenuNest.Application.UseCases.ShoppingList;
using MenuNest.Application.UseCases.ShoppingList.ListShoppingLists;
using MenuNest.Application.UseCases.ShoppingList.GetShoppingListDetail;
using MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;
using MenuNest.Application.UseCases.ShoppingList.DeleteShoppingList;
using MenuNest.Application.UseCases.ShoppingList.CompleteShoppingList;
using MenuNest.Application.UseCases.ShoppingList.AddShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.DeleteShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.UnbuyShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.RegenerateShoppingList;
using MenuNest.Domain.Enums;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class ShoppingListToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly ShoppingListTools _sut;

    public ShoppingListToolsTests() => _sut = new ShoppingListTools(_mediator.Object);

    [Fact]
    public async Task list_shopping_lists_sends_ListShoppingListsQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListShoppingListsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<ShoppingListDto>>(new List<ShoppingListDto>()));
        await _sut.list_shopping_lists(null, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.IsAny<ListShoppingListsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task list_shopping_lists_passes_status_filter_to_query()
    {
        _mediator
            .Setup(m => m.Send(It.Is<ListShoppingListsQuery>(q => q.Status == ShoppingListStatus.Active), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<ShoppingListDto>>(new List<ShoppingListDto>()));
        await _sut.list_shopping_lists(ShoppingListStatus.Active, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<ListShoppingListsQuery>(q => q.Status == ShoppingListStatus.Active), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task get_shopping_list_sends_GetShoppingListDetailQuery_with_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<GetShoppingListDetailQuery>(q => q.Id == id), It.IsAny<CancellationToken>()))
            .Returns<GetShoppingListDetailQuery, CancellationToken>((_, _) => new ValueTask<ShoppingListDetailDto>((ShoppingListDetailDto)default!));
        await _sut.get_shopping_list(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<GetShoppingListDetailQuery>(q => q.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_shopping_list_sends_CreateShoppingListCommand_with_name_and_dates()
    {
        const string name = "Weekly Shop";
        var fromDate = new DateOnly(2026, 6, 1);
        var toDate = new DateOnly(2026, 6, 7);
        _mediator
            .Setup(m => m.Send(It.Is<CreateShoppingListCommand>(c => c.Name == name && c.FromDate == fromDate && c.ToDate == toDate), It.IsAny<CancellationToken>()))
            .Returns<CreateShoppingListCommand, CancellationToken>((_, _) => new ValueTask<ShoppingListDto>((ShoppingListDto)default!));
        await _sut.create_shopping_list(name, fromDate, toDate, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CreateShoppingListCommand>(c => c.Name == name && c.FromDate == fromDate && c.ToDate == toDate), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_shopping_list_sends_DeleteShoppingListCommand_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteShoppingListCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.delete_shopping_list(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<DeleteShoppingListCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task complete_shopping_list_sends_CompleteShoppingListCommand_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<CompleteShoppingListCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns<CompleteShoppingListCommand, CancellationToken>((_, _) => new ValueTask<ShoppingListDto>((ShoppingListDto)default!));
        await _sut.complete_shopping_list(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CompleteShoppingListCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task add_shopping_list_item_sends_AddShoppingListItemCommand_with_correct_properties()
    {
        var listId = Guid.NewGuid();
        var ingredientId = Guid.NewGuid();
        const decimal quantity = 2.5m;
        _mediator
            .Setup(m => m.Send(It.Is<AddShoppingListItemCommand>(c => c.ListId == listId && c.IngredientId == ingredientId && c.Quantity == quantity), It.IsAny<CancellationToken>()))
            .Returns<AddShoppingListItemCommand, CancellationToken>((_, _) => new ValueTask<ShoppingListItemDto>((ShoppingListItemDto)default!));
        await _sut.add_shopping_list_item(listId, ingredientId, quantity, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<AddShoppingListItemCommand>(c => c.ListId == listId && c.IngredientId == ingredientId && c.Quantity == quantity), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_shopping_list_item_sends_DeleteShoppingListItemCommand_with_list_and_item_ids()
    {
        var listId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteShoppingListItemCommand>(c => c.ListId == listId && c.ItemId == itemId), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.delete_shopping_list_item(listId, itemId, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<DeleteShoppingListItemCommand>(c => c.ListId == listId && c.ItemId == itemId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task buy_shopping_list_item_sends_BuyShoppingListItemCommand_with_list_and_item_ids()
    {
        var listId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<BuyShoppingListItemCommand>(c => c.ListId == listId && c.ItemId == itemId), It.IsAny<CancellationToken>()))
            .Returns<BuyShoppingListItemCommand, CancellationToken>((_, _) => new ValueTask<ShoppingListItemDto>((ShoppingListItemDto)default!));
        await _sut.buy_shopping_list_item(listId, itemId, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<BuyShoppingListItemCommand>(c => c.ListId == listId && c.ItemId == itemId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task unbuy_shopping_list_item_sends_UnbuyShoppingListItemCommand_with_list_and_item_ids()
    {
        var listId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<UnbuyShoppingListItemCommand>(c => c.ListId == listId && c.ItemId == itemId), It.IsAny<CancellationToken>()))
            .Returns<UnbuyShoppingListItemCommand, CancellationToken>((_, _) => new ValueTask<ShoppingListItemDto>((ShoppingListItemDto)default!));
        await _sut.unbuy_shopping_list_item(listId, itemId, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<UnbuyShoppingListItemCommand>(c => c.ListId == listId && c.ItemId == itemId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task regenerate_shopping_list_sends_RegenerateShoppingListCommand_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<RegenerateShoppingListCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns<RegenerateShoppingListCommand, CancellationToken>((_, _) => new ValueTask<ShoppingListDetailDto>((ShoppingListDetailDto)default!));
        await _sut.regenerate_shopping_list(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<RegenerateShoppingListCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
