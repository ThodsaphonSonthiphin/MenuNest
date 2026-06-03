using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MenuNest.Domain.Exceptions;
using ModelContextProtocol.Protocol;

namespace MenuNest.McpServer.UnitTests;

public class McpToolErrorMapperTests
{
    [Fact]
    public async Task Passes_through_successful_result()
    {
        var ok = new CallToolResult { IsError = false };

        var result = await McpToolErrorMapper.GuardAsync(
            "list_recipes", services: null, () => new ValueTask<CallToolResult>(ok));

        result.Should().BeSameAs(ok);
    }

    [Fact]
    public async Task Translates_DomainException_to_error_result()
    {
        const string message = "You must join or create a family before using this feature.";

        var result = await McpToolErrorMapper.GuardAsync(
            "list_recipes", services: null,
            () => throw new DomainException(message));

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle()
            .Which.Should().BeOfType<TextContentBlock>()
            .Which.Text.Should().Be(message);
    }

    [Fact]
    public async Task Translates_ValidationException_to_error_result()
    {
        var ex = new ValidationException(new[] { new ValidationFailure("Name", "Name is required.") });

        var result = await McpToolErrorMapper.GuardAsync(
            "create_recipe", services: null, () => throw ex);

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>();
    }

    [Fact]
    public async Task Lets_unexpected_exceptions_propagate()
    {
        Func<Task> act = async () => await McpToolErrorMapper.GuardAsync(
            "list_recipes", services: null,
            () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
