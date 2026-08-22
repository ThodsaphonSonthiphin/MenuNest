using FluentAssertions;
using Mediator;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.CorrectBalance;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests;

/// <summary>
/// Exercises <c>BudgetTools.correct_account_balance</c> end-to-end against a
/// real <see cref="CorrectBalanceHandler"/> and a real (InMemory) DbContext —
/// not a mocked mediator return value — so these tests can assert on what
/// actually landed in <c>BudgetTransactions</c>, not just on the DTO a wrong
/// handler could fake. The <see cref="IMediator"/> mock here only forwards
/// <see cref="CorrectBalanceCommand"/> to the real handler; it does not stub
/// a canned response (contrast with the shallow forwarding checks in
/// <c>Tools/BudgetToolsTests.cs</c>).
/// </summary>
public sealed class BudgetToolsCorrectBalanceTests : IDisposable
{
    // The app's one real time zone (menunest-189).
    private const string Bkk = "Asia/Bangkok";

    private readonly HandlerTestFixture _fx = new();
    private readonly BudgetTools _sut;
    private readonly Guid _accountId;
    private readonly CancellationToken _ct = CancellationToken.None;

    public BudgetToolsCorrectBalanceTests()
    {
        var handler = new CorrectBalanceHandler(
            _fx.Db, _fx.UserProvisioner.Object, new CorrectBalanceValidator(), _fx.Clock);

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<CorrectBalanceCommand>(), It.IsAny<CancellationToken>()))
            .Returns<CorrectBalanceCommand, CancellationToken>((cmd, ct) => handler.Handle(cmd, ct));
        _sut = new BudgetTools(mediator.Object);

        // Seeded: one opening-balance transaction dated "today" (FixedClock =
        // 2026-01-01) so the derived balance is 2,400.
        var acc = BudgetAccount.Create(_fx.Family.Id, "Cash", BudgetAccountType.Cash, 0m, 0);
        _fx.Db.BudgetAccounts.Add(acc);
        _fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            _fx.Family.Id, acc.Id, categoryId: null, amount: 2_400m,
            date: new DateOnly(2026, 1, 1), notes: "Opening balance", createdByUserId: _fx.User.Id));
        _fx.Db.SaveChanges();
        _accountId = acc.Id;
    }

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task An_unconfirmed_call_writes_nothing_and_names_the_numbers()
    {
        var result = await _sut.correct_account_balance(
            _accountId, actualBalance: 3000m, confirmed: false, date: null, notes: null, timeZoneId: Bkk, _ct);

        result.Written.Should().BeFalse();
        result.DerivedBalance.Should().Be(2400m);
        result.Difference.Should().Be(600m);
        result.Message.Should().Contain("2,400").And.Contain("600");

        // The absence of a write, not just the returned flag — a handler
        // that lied about Written while still writing must fail this.
        _fx.Db.BudgetTransactions.Should().ContainSingle(t => t.Notes == "Opening balance");
        _fx.Db.BudgetTransactions.Should().NotContain(t => t.Notes == "Balance correction");
    }

    [Fact]
    public async Task A_confirmed_call_writes_one_uncategorised_correction()
    {
        var result = await _sut.correct_account_balance(
            _accountId, actualBalance: 3000m, confirmed: true, date: null, notes: null, timeZoneId: Bkk, _ct);

        result.Written.Should().BeTrue();
        var tx = _fx.Db.BudgetTransactions.Single(t => t.Notes == "Balance correction");
        tx.Amount.Should().Be(600m);
        tx.CategoryId.Should().BeNull();     // lands in Ready to Assign, no quarantine
        tx.Notes.Should().Be("Balance correction");
    }

    [Fact]
    public async Task A_zero_difference_writes_nothing_and_is_not_an_error()
    {
        var result = await _sut.correct_account_balance(
            _accountId, actualBalance: 2400m, confirmed: true, date: null, notes: null, timeZoneId: Bkk, _ct);

        result.Written.Should().BeFalse();
        _fx.Db.BudgetTransactions.Should().ContainSingle(t => t.Notes == "Opening balance");
        _fx.Db.BudgetTransactions.Should().NotContain(t => t.Notes == "Balance correction");
    }

    [Fact]
    public async Task The_supplied_date_lands_the_correction_in_that_month()
    {
        await _sut.correct_account_balance(
            _accountId, 3000m, confirmed: true, date: new DateOnly(2026, 7, 31), notes: null, timeZoneId: Bkk, _ct);

        _fx.Db.BudgetTransactions.Single(t => t.Notes == "Balance correction").Date
            .Should().Be(new DateOnly(2026, 7, 31));
    }

    [Fact]
    public async Task A_custom_note_replaces_the_default_correction_note()
    {
        await _sut.correct_account_balance(
            _accountId, 3000m, confirmed: true, date: null, notes: "Found cash in the drawer", timeZoneId: Bkk, _ct);

        _fx.Db.BudgetTransactions.Single(t => t.CategoryId == null && t.Amount == 600m)
            .Notes.Should().Be("Found cash in the drawer");
    }

    [Fact]
    public async Task A_missing_time_zone_is_rejected_rather_than_silently_read_as_UTC()
    {
        var act = () => _sut.correct_account_balance(
            _accountId, actualBalance: 3000m, confirmed: false, date: null, notes: null, timeZoneId: null, _ct);

        await act.Should().ThrowAsync<Exception>();
        _fx.Db.BudgetTransactions.Should().ContainSingle(t => t.Notes == "Opening balance");
    }
}
