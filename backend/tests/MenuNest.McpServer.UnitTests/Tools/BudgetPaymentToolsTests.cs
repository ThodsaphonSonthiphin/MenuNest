using Mediator;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Payments.MakePayment;
using MenuNest.Application.UseCases.Budget.Payments.UpdatePayment;
using MenuNest.Application.UseCases.Budget.Payments.DeletePayment;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

// menunest-213: every function Issue #112 adds must be reachable over MCP.
// These three tools forward their arguments verbatim to the command the
// Application handlers already validate — see PaymentCategoryRule for the
// CategoryId rules asserted here (required for Loan, refused for Credit).
public class BudgetPaymentToolsTests
{
    private const string Tz = "Asia/Bangkok";

    private readonly Mock<IMediator> _mediator = new();
    private readonly BudgetTools _sut;

    public BudgetPaymentToolsTests() => _sut = new BudgetTools(_mediator.Object);

    [Fact]
    public async Task pay_account_sends_MakePaymentCommand_with_correct_fields()
    {
        var fromAccountId = Guid.NewGuid();
        var toAccountId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 15);
        _mediator
            .Setup(m => m.Send(It.Is<MakePaymentCommand>(c =>
                c.FromAccountId == fromAccountId &&
                c.ToAccountId == toAccountId &&
                c.Amount == 1500m &&
                c.Date == date &&
                c.Notes == "August payment" &&
                c.TimeZoneId == Tz &&
                c.CategoryId == null), It.IsAny<CancellationToken>()))
            .Returns<MakePaymentCommand, CancellationToken>((_, _) => new ValueTask<PaymentDto>((PaymentDto)default!));

        await _sut.pay_account(fromAccountId, toAccountId, 1500m, date, "August payment", Tz, null, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<MakePaymentCommand>(c =>
            c.FromAccountId == fromAccountId &&
            c.ToAccountId == toAccountId &&
            c.Amount == 1500m &&
            c.Date == date &&
            c.Notes == "August payment" &&
            c.TimeZoneId == Tz &&
            c.CategoryId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task pay_account_forwards_categoryId_for_a_loan_payment()
    {
        var fromAccountId = Guid.NewGuid();
        var toAccountId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<MakePaymentCommand>(c => c.CategoryId == categoryId), It.IsAny<CancellationToken>()))
            .Returns<MakePaymentCommand, CancellationToken>((_, _) => new ValueTask<PaymentDto>((PaymentDto)default!));

        await _sut.pay_account(fromAccountId, toAccountId, 500m, null, null, Tz, categoryId, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<MakePaymentCommand>(c => c.CategoryId == categoryId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task update_payment_sends_UpdatePaymentCommand_with_correct_fields()
    {
        var paymentId = Guid.NewGuid();
        var fromAccountId = Guid.NewGuid();
        var toAccountId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 20);
        _mediator
            .Setup(m => m.Send(It.Is<UpdatePaymentCommand>(c =>
                c.PaymentId == paymentId &&
                c.FromAccountId == fromAccountId &&
                c.ToAccountId == toAccountId &&
                c.Amount == 2000m &&
                c.Date == date &&
                c.Notes == "Corrected" &&
                c.CategoryId == categoryId), It.IsAny<CancellationToken>()))
            .Returns<UpdatePaymentCommand, CancellationToken>((_, _) => new ValueTask<PaymentDto>((PaymentDto)default!));

        await _sut.update_payment(paymentId, fromAccountId, toAccountId, 2000m, date, "Corrected", categoryId, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<UpdatePaymentCommand>(c =>
            c.PaymentId == paymentId &&
            c.FromAccountId == fromAccountId &&
            c.ToAccountId == toAccountId &&
            c.Amount == 2000m &&
            c.Date == date &&
            c.Notes == "Corrected" &&
            c.CategoryId == categoryId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_payment_sends_DeletePaymentCommand_with_id()
    {
        var paymentId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeletePaymentCommand>(c => c.PaymentId == paymentId), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));

        await _sut.delete_payment(paymentId, CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<DeletePaymentCommand>(c => c.PaymentId == paymentId), It.IsAny<CancellationToken>()), Times.Once);
    }
}
