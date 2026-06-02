using Mediator;
using MenuNest.Application.UseCases.Ingredients;
using MenuNest.Application.UseCases.Ingredients.ListIngredients;
using MenuNest.Application.UseCases.Ingredients.CreateIngredient;
using MenuNest.Application.UseCases.Ingredients.UpdateIngredient;
using MenuNest.Application.UseCases.Ingredients.DeleteIngredient;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class IngredientToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly IngredientTools _sut;

    public IngredientToolsTests() => _sut = new IngredientTools(_mediator.Object);

    [Fact]
    public async Task list_ingredients_sends_ListIngredientsQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListIngredientsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<IngredientDto>>(new List<IngredientDto>()));
        await _sut.list_ingredients(CancellationToken.None);
        _mediator.Verify(m => m.Send(It.IsAny<ListIngredientsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_ingredient_sends_CreateIngredientCommand_with_name_and_unit()
    {
        const string name = "Flour";
        const string unit = "g";
        _mediator
            .Setup(m => m.Send(It.Is<CreateIngredientCommand>(c => c.Name == name && c.Unit == unit), It.IsAny<CancellationToken>()))
            .Returns<CreateIngredientCommand, CancellationToken>((_, _) => new ValueTask<IngredientDto>(new IngredientDto(Guid.NewGuid(), name, unit)));
        await _sut.create_ingredient(name, unit, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CreateIngredientCommand>(c => c.Name == name && c.Unit == unit), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task update_ingredient_sends_UpdateIngredientCommand_with_correct_id_name_unit()
    {
        var id = Guid.NewGuid();
        const string name = "Sugar";
        const string unit = "kg";
        _mediator
            .Setup(m => m.Send(It.Is<UpdateIngredientCommand>(c => c.Id == id && c.Name == name && c.Unit == unit), It.IsAny<CancellationToken>()))
            .Returns<UpdateIngredientCommand, CancellationToken>((_, _) => new ValueTask<IngredientDto>(new IngredientDto(id, name, unit)));
        await _sut.update_ingredient(id, name, unit, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<UpdateIngredientCommand>(c => c.Id == id && c.Name == name && c.Unit == unit), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_ingredient_sends_DeleteIngredientCommand_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteIngredientCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.delete_ingredient(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<DeleteIngredientCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
