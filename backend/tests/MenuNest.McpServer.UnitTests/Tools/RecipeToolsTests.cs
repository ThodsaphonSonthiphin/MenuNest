using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Recipes;
using MenuNest.Application.UseCases.Recipes.ListRecipes;
using MenuNest.Application.UseCases.Recipes.GetRecipe;
using MenuNest.Application.UseCases.Recipes.CreateRecipe;
using MenuNest.Application.UseCases.Recipes.DeleteRecipe;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class RecipeToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly RecipeTools _sut;

    public RecipeToolsTests() => _sut = new RecipeTools(_mediator.Object);

    [Fact]
    public async Task list_recipes_sends_ListRecipesQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListRecipesQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<RecipeSummaryDto>>(new List<RecipeSummaryDto>()));

        await _sut.list_recipes(CancellationToken.None);

        _mediator.Verify(m => m.Send(It.IsAny<ListRecipesQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task get_recipe_sends_GetRecipeQuery_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<GetRecipeQuery>(q => q.Id == id), It.IsAny<CancellationToken>()))
            .Returns<GetRecipeQuery, CancellationToken>((_, _) => new ValueTask<RecipeDetailDto>((RecipeDetailDto)default!));

        await _sut.get_recipe(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<GetRecipeQuery>(q => q.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_recipe_sends_CreateRecipeCommand_with_name()
    {
        const string name = "Carbonara";
        _mediator
            .Setup(m => m.Send(It.Is<CreateRecipeCommand>(c => c.Name == name), It.IsAny<CancellationToken>()))
            .Returns<CreateRecipeCommand, CancellationToken>((_, _) => new ValueTask<RecipeDetailDto>((RecipeDetailDto)default!));

        await _sut.create_recipe(name, null, Array.Empty<RecipeIngredientInput>(), CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<CreateRecipeCommand>(c => c.Name == name), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_recipe_sends_DeleteRecipeCommand_with_correct_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteRecipeCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));

        await _sut.delete_recipe(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<DeleteRecipeCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
