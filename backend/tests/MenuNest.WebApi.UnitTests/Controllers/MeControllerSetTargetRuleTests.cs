using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Writing.SetActiveTargetRule;
using MenuNest.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Controllers;

/// <summary>
/// Wire-boundary test for PUT /api/me/target-rule. The SetTargetRule action
/// must correctly map SetTargetRuleRequest.Rule into SetActiveTargetRuleCommand
/// and map the command's response back into ActiveTargetRuleResponse. A test that
/// constructs SetActiveTargetRuleCommand directly (bypassing the controller)
/// proves nothing about the controller's own binding and shape conversion.
/// </summary>
public sealed class MeControllerSetTargetRuleTests
{
    [Fact]
    public async Task SetTargetRule_maps_request_rule_to_command_and_returns_response()
    {
        var mediator = new Mock<IMediator>();
        SetActiveTargetRuleCommand? captured = null;
        var responseRule = "third-person singular -s";

        mediator
            .Setup(m => m.Send(It.IsAny<SetActiveTargetRuleCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ICommand<string?>, CancellationToken>((c, _) => captured = (SetActiveTargetRuleCommand)c)
            .Returns(new ValueTask<string?>(result: responseRule));

        var controller = new MeController(mediator.Object);
        var request = new SetTargetRuleRequest("third-person singular -s");

        var result = await controller.SetTargetRule(request, CancellationToken.None);

        captured.Should().NotBeNull("the controller must send exactly one SetActiveTargetRuleCommand");
        captured!.Rule.Should().Be("third-person singular -s",
            "the request.Rule must be passed through to the command unchanged");

        mediator.Verify(m => m.Send(It.IsAny<SetActiveTargetRuleCommand>(), It.IsAny<CancellationToken>()), Times.Once);

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var response = okResult!.Value as ActiveTargetRuleResponse;
        response.Should().NotBeNull();
        response!.ActiveTargetRule.Should().Be("third-person singular -s",
            "the mediator's response must be returned in the ActiveTargetRuleResponse");
    }

    [Fact]
    public async Task SetTargetRule_handles_null_rule_for_clearing()
    {
        var mediator = new Mock<IMediator>();
        SetActiveTargetRuleCommand? captured = null;

        mediator
            .Setup(m => m.Send(It.IsAny<SetActiveTargetRuleCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ICommand<string?>, CancellationToken>((c, _) => captured = (SetActiveTargetRuleCommand)c)
            .Returns(new ValueTask<string?>(result: (string?)null));

        var controller = new MeController(mediator.Object);
        var request = new SetTargetRuleRequest(null);

        var result = await controller.SetTargetRule(request, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Rule.Should().BeNull("null is a legal value meaning clear the rule");

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var response = okResult!.Value as ActiveTargetRuleResponse;
        response.Should().NotBeNull();
        response!.ActiveTargetRule.Should().BeNull("clearing the rule returns null");
    }
}
