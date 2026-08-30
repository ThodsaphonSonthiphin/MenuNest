using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Accounts.UpdateAccount;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Application.UseCases.Budget.Payments.MakePayment;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

/// <summary>
/// menunest-210 / menunest-212: an Account's lifecycle keeps its Payment
/// envelope honest — a rename cascades to the envelope's name, and a Credit
/// card that still owes money cannot be closed (menunest-205's own guard
/// forbids deleting the envelope for exactly the same reason; closing the
/// account is the side door to the same outcome). All dates land in the
/// fixture's fixed "current" month (Jan 2026) so <see cref="SummaryAsync"/>
/// exercises the same month the assignments/transactions are seeded into.
/// </summary>
public class CreditAccountLifecycleTests
{
    private const string Bkk = "Asia/Bangkok";
    private const int Year = 2026;
    private const int Month = 1;
    private static readonly DateOnly TxDate = new(2026, 1, 10);

    private sealed record World(HandlerTestFixture Fx, Guid CardId, Guid FoodId);

    private static async Task<World> Seed()
    {
        var fx = new HandlerTestFixture();

        var card = BudgetAccount.Create(fx.Family.Id, "KBank", BudgetAccountType.Credit, 0m, 0);
        fx.Db.BudgetAccounts.Add(card);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var food = BudgetCategory.Create(fx.Family.Id, group.Id, "Food", null, 0);
        fx.Db.BudgetCategories.Add(food);
        await fx.Db.SaveChangesAsync();

        // Provisions the card's Payment envelope, exactly as the real read path does.
        await new PaymentEnvelopeProvisioner(fx.Db).EnsureForFamilyAndSaveAsync(fx.Family.Id, default);

        return new World(fx, card.Id, food.Id);
    }

    /// <summary>Inserts a MonthlyAssignment row for the given category/month.</summary>
    private static async Task AssignTo(World w, Guid categoryId, decimal amount)
    {
        w.Fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(w.Fx.Family.Id, categoryId, Year, Month, amount));
        await w.Fx.Db.SaveChangesAsync();
    }

    /// <summary>The card's Payment envelope's category id.</summary>
    private static Guid PaymentEnvelopeId(World w) =>
        w.Fx.Db.BudgetCategories.Single(c => c.PaymentForAccountId == w.CardId).Id;

    private static async Task AddTx(World w, Guid accountId, Guid? categoryId, decimal amount)
    {
        w.Fx.Db.BudgetTransactions.Add(
            BudgetTransaction.Create(w.Fx.Family.Id, accountId, categoryId, amount, TxDate, null, w.Fx.User.Id));
        await w.Fx.Db.SaveChangesAsync();
    }

    private static async Task MakePayment(World w, decimal amount)
    {
        var cash = BudgetAccount.Create(w.Fx.Family.Id, "Wallet", BudgetAccountType.Cash, 0m, 0);
        w.Fx.Db.BudgetAccounts.Add(cash);
        await w.Fx.Db.SaveChangesAsync();

        var sut = new MakePaymentHandler(w.Fx.Db, w.Fx.UserProvisioner.Object, new MakePaymentValidator(), w.Fx.Clock);
        await sut.Handle(
            new MakePaymentCommand(cash.Id, w.CardId, amount, TxDate, null, Bkk, CategoryId: null),
            CancellationToken.None);
    }

    private static async Task<BudgetAccountDto> UpdateAccount(World w, Guid accountId, string name, bool isClosed)
    {
        var sut = new UpdateAccountHandler(w.Fx.Db, w.Fx.UserProvisioner.Object, new UpdateAccountValidator());
        return await sut.Handle(new UpdateAccountCommand(accountId, name, 0, isClosed), CancellationToken.None);
    }

    private static async Task<MonthlySummaryDto> SummaryAsync(World w)
    {
        var sut = new GetMonthlySummaryHandler(
            w.Fx.Db, w.Fx.UserProvisioner.Object, new AllowanceFreezer(w.Fx.Db),
            new PaymentEnvelopeProvisioner(w.Fx.Db), w.Fx.Clock);
        return await sut.Handle(new GetMonthlySummaryQuery(Year, Month, Bkk), CancellationToken.None);
    }

    [Fact]
    public async Task Renaming_the_card_renames_its_payment_envelope()
    {
        var w = await Seed(); using var _ = w.Fx;

        await UpdateAccount(w, w.CardId, "KBank Platinum", isClosed: false);

        var envelope = w.Fx.Db.BudgetCategories.Single(c => c.PaymentForAccountId == w.CardId);
        envelope.Name.Should().Be("จ่ายบัตร KBank Platinum");
    }

    [Fact]
    public async Task Closing_a_card_that_still_owes_is_refused()
    {
        var w = await Seed(); using var _ = w.Fx;
        await AddTx(w, w.CardId, w.FoodId, -500m);

        var act = async () => await UpdateAccount(w, w.CardId, "KBank Renamed", isClosed: true);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Be("ยังจ่ายบัตรไม่ครบ — ปิดบัญชีไม่ได้");

        var reloaded = w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CardId);
        reloaded.IsClosed.Should().BeFalse("a refused close must not leave the account half-closed");
        reloaded.Name.Should().Be("KBank",
            "the debt check must run BEFORE any mutation — a refusal must not leave a half-applied rename behind it");
    }

    [Fact]
    public async Task Closing_a_settled_card_is_allowed()
    {
        var w = await Seed(); using var _ = w.Fx;
        await AddTx(w, w.CardId, w.FoodId, -500m);
        await MakePayment(w, 500m);

        await UpdateAccount(w, w.CardId, "KBank", isClosed: true);

        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CardId).IsClosed.Should().BeTrue();
        w.Fx.Db.BudgetCategories.Single(c => c.PaymentForAccountId == w.CardId).IsHidden.Should().BeTrue(
            "closing hides the envelope (menunest-210), bypassing Hide()'s own payment-envelope guard");
    }

    // menunest-210's correction: totalEnvelopeAvailableAllCats walks HIDDEN
    // categories too, so hiding alone would leave the remainder locked.
    [Fact]
    public async Task Closing_a_settled_card_returns_its_leftover_money_to_Ready_to_Assign()
    {
        var w = await Seed(); using var _ = w.Fx;
        await AssignTo(w, PaymentEnvelopeId(w), 1_000m); // over-fund it; balance stays 0
        var whileOpen = (await SummaryAsync(w)).ReadyToAssign;

        await UpdateAccount(w, w.CardId, "KBank", isClosed: true);

        (await SummaryAsync(w)).ReadyToAssign.Should().Be(whileOpen + 1_000m);
    }

    [Fact]
    public async Task Reopening_the_card_takes_the_money_back_out()
    {
        var w = await Seed(); using var _ = w.Fx;
        await AssignTo(w, PaymentEnvelopeId(w), 1_000m);
        var whileOpen = (await SummaryAsync(w)).ReadyToAssign;

        await UpdateAccount(w, w.CardId, "KBank", isClosed: true);
        await UpdateAccount(w, w.CardId, "KBank", isClosed: false);

        (await SummaryAsync(w)).ReadyToAssign.Should().Be(whileOpen,
            "the MonthlyAssignment rows are untouched, so reopening is exactly reversible");
        w.Fx.Db.BudgetCategories.Single(c => c.PaymentForAccountId == w.CardId).IsHidden.Should().BeFalse();
    }

    // Pinned to the exact loan-specific string (not the card one, and not a
    // wildcard) so a regression that reunifies the two messages — or that
    // just reuses the card wording for a loan — fails loudly. menunest-212's
    // vocabulary: a loan is "จ่ายค่างวด" (an instalment), never "จ่ายบัตร"
    // (a card) — a Loan owner must not be told they haven't paid a "card".
    [Fact]
    public async Task Closing_a_loan_that_still_owes_is_refused()
    {
        using var fx = new HandlerTestFixture();
        var loan = BudgetAccount.Create(fx.Family.Id, "Car Loan", BudgetAccountType.Loan, 0m, 0);
        fx.Db.BudgetAccounts.Add(loan);
        await fx.Db.SaveChangesAsync();
        fx.Db.BudgetTransactions.Add(
            BudgetTransaction.Create(fx.Family.Id, loan.Id, null, -1000m, TxDate, null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateAccountHandler(fx.Db, fx.UserProvisioner.Object, new UpdateAccountValidator());
        var act = async () => await sut.Handle(
            new UpdateAccountCommand(loan.Id, "Car Loan", 0, IsClosed: true), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Be("ยังจ่ายค่างวดไม่ครบ — ปิดบัญชีไม่ได้");
    }

    // ── Regression: an ordinary Cash account must be entirely unaffected ────

    [Fact]
    public async Task Closing_a_cash_account_is_unaffected()
    {
        using var fx = new HandlerTestFixture();
        var acc = BudgetAccount.Create(fx.Family.Id, "Wallet", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateAccountHandler(fx.Db, fx.UserProvisioner.Object, new UpdateAccountValidator());

        var closed = await sut.Handle(
            new UpdateAccountCommand(acc.Id, "Wallet", 0, IsClosed: true), CancellationToken.None);
        closed.IsClosed.Should().BeTrue();

        var reopened = await sut.Handle(
            new UpdateAccountCommand(acc.Id, "Wallet", 0, IsClosed: false), CancellationToken.None);
        reopened.IsClosed.Should().BeFalse();
    }

    [Fact]
    public async Task Renaming_a_cash_account_does_not_error_looking_for_an_envelope_that_does_not_exist()
    {
        using var fx = new HandlerTestFixture();
        var acc = BudgetAccount.Create(fx.Family.Id, "Wallet", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateAccountHandler(fx.Db, fx.UserProvisioner.Object, new UpdateAccountValidator());

        var result = await sut.Handle(
            new UpdateAccountCommand(acc.Id, "New Wallet Name", 0, IsClosed: false), CancellationToken.None);

        result.Name.Should().Be("New Wallet Name");
    }
}
