using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Payments.DeletePayment;
using MenuNest.Application.UseCases.Budget.Payments.MakePayment;
using MenuNest.Application.UseCases.Budget.Payments.UpdatePayment;
using MenuNest.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Controllers;

/// <summary>
/// Wire-boundary tests for the three payment routes issue #112 adds
/// (menunest-204, menunest-209, menunest-213), the project spec §9 names for
/// exactly this and which had no test.
///
/// What they defend is not the handlers — those are covered in
/// <c>MenuNest.Application.UnitTests</c> — but the by-hand re-ordering in
/// <see cref="BudgetController.MakePayment"/>, which maps SEVEN positional
/// arguments from <see cref="MakePaymentRequest"/> into
/// <see cref="MakePaymentCommand"/>. Both records carry two
/// <c>Guid</c> account ids, a <c>decimal</c>, two nullable strings and a
/// nullable <c>Guid</c>, so transposing FromAccountId with ToAccountId — the
/// transposition that pays the wrong account and doubles a debt instead of
/// clearing it — COMPILES, and nothing downstream can tell it happened. Every
/// field is therefore given a distinct value here and asserted individually;
/// asserting "a MakePaymentCommand was sent" would prove nothing at all.
/// </summary>
public sealed class BudgetControllerPaymentRoutesTests
{
    private static readonly Guid From = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid To = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Category = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PaymentId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static PaymentDto Dto() => new(
        PaymentId, From, "เงินสด", To, "ผ่อนรถ", 1_500m, new DateOnly(2026, 6, 15), "งวดมิถุนายน");

    [Fact]
    public async Task MakePayment_maps_every_request_field_onto_the_command_unshuffled()
    {
        var mediator = new Mock<IMediator>();
        MakePaymentCommand? captured = null;
        mediator
            .Setup(m => m.Send(It.IsAny<MakePaymentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ICommand<PaymentDto>, CancellationToken>((c, _) => captured = (MakePaymentCommand)c)
            .Returns(new ValueTask<PaymentDto>(Dto()));

        var controller = new BudgetController(mediator.Object);
        var request = new MakePaymentRequest(
            FromAccountId: From, ToAccountId: To, Amount: 1_500m,
            Date: new DateOnly(2026, 6, 15), Notes: "งวดมิถุนายน",
            TimeZoneId: "Asia/Bangkok", CategoryId: Category);

        var result = await controller.MakePayment(request, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.FromAccountId.Should().Be(From, "the paying account must not swap with the account being paid");
        captured.ToAccountId.Should().Be(To);
        captured.Amount.Should().Be(1_500m);
        captured.Date.Should().Be(new DateOnly(2026, 6, 15));
        captured.Notes.Should().Be("งวดมิถุนายน", "Notes must not swap with TimeZoneId — both are string?");
        captured.TimeZoneId.Should().Be("Asia/Bangkok");
        captured.CategoryId.Should().Be(Category, "menunest-214: this is the Envelope funding a loan instalment");

        mediator.Verify(m => m.Send(It.IsAny<MakePaymentCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        (result.Result as OkObjectResult)!.Value.Should().BeOfType<PaymentDto>();
    }

    // menunest-204: Date and TimeZoneId are both optional — the handler defaults
    // the date to the viewer's local today. A controller that substituted a
    // value of its own would silently date payments in UTC.
    [Fact]
    public async Task MakePayment_passes_a_null_date_and_null_category_straight_through()
    {
        var mediator = new Mock<IMediator>();
        MakePaymentCommand? captured = null;
        mediator
            .Setup(m => m.Send(It.IsAny<MakePaymentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ICommand<PaymentDto>, CancellationToken>((c, _) => captured = (MakePaymentCommand)c)
            .Returns(new ValueTask<PaymentDto>(Dto()));

        await new BudgetController(mediator.Object).MakePayment(
            new MakePaymentRequest(From, To, 500m, null, null, "Asia/Bangkok"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Date.Should().BeNull("the handler resolves the viewer's local today, not the controller");
        captured.Notes.Should().BeNull();
        captured.CategoryId.Should().BeNull("a Credit card payment is refused a category by PaymentCategoryRule");
    }

    [Fact]
    public async Task UpdatePayment_maps_the_route_id_and_every_body_field_onto_the_command()
    {
        var mediator = new Mock<IMediator>();
        UpdatePaymentCommand? captured = null;
        mediator
            .Setup(m => m.Send(It.IsAny<UpdatePaymentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ICommand<PaymentDto>, CancellationToken>((c, _) => captured = (UpdatePaymentCommand)c)
            .Returns(new ValueTask<PaymentDto>(Dto()));

        var controller = new BudgetController(mediator.Object);
        var request = new UpdatePaymentRequest(
            FromAccountId: From, ToAccountId: To, Amount: 1_500m,
            Date: new DateOnly(2026, 6, 15), Notes: "งวดมิถุนายน", CategoryId: Category);

        var result = await controller.UpdatePayment(PaymentId, request, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.PaymentId.Should().Be(PaymentId,
            "the id comes from the ROUTE — a body field of the same type would bind silently");
        captured.FromAccountId.Should().Be(From);
        captured.ToAccountId.Should().Be(To);
        captured.Amount.Should().Be(1_500m);
        captured.Date.Should().Be(new DateOnly(2026, 6, 15));
        captured.Notes.Should().Be("งวดมิถุนายน");
        captured.CategoryId.Should().Be(Category);

        (result.Result as OkObjectResult)!.Value.Should().BeOfType<PaymentDto>();
    }

    [Fact]
    public async Task DeletePayment_sends_the_route_id_and_returns_204()
    {
        var mediator = new Mock<IMediator>();
        DeletePaymentCommand? captured = null;
        mediator
            .Setup(m => m.Send(It.IsAny<DeletePaymentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ICommand<Unit>, CancellationToken>((c, _) => captured = (DeletePaymentCommand)c)
            .Returns(new ValueTask<Unit>(Unit.Value));

        var result = await new BudgetController(mediator.Object)
            .DeletePayment(PaymentId, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.PaymentId.Should().Be(PaymentId);
        // menunest-209: the pair is deleted whole; there is no body to return.
        result.Should().BeOfType<NoContentResult>();
        mediator.Verify(m => m.Send(It.IsAny<DeletePaymentCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
