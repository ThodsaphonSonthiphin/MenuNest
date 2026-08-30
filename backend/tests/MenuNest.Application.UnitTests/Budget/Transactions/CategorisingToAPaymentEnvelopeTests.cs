using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Application.UseCases.Budget.Transactions.CreateTransaction;
using MenuNest.Application.UseCases.Budget.Transactions.UpdateTransaction;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Transactions;

/// <summary>
/// menunest-203's own stated failure, reached through the ORDINARY transaction
/// handlers rather than through a payment.
///
/// A Payment envelope's Available is DERIVED — <see cref="Monthly.PaymentEnvelopeMath"/>
/// reads only its own card's rows. <c>GetMonthlySummaryHandler.EnvelopeNumbers</c>
/// therefore BRANCHES on <c>PaymentForAccountId</c>: for a Payment envelope it never
/// touches <c>allTx</c> at all. So a transaction on a NON-Credit account that is
/// categorised to a Payment envelope is invisible to both halves of the model — the
/// derivation (wrong account) and the ordinary walk (wrong branch) — while the cash
/// account's balance still falls. The money leaves Ready to Assign and lands nowhere.
///
/// The hand-derived trace, on the world <see cref="Seed"/> builds:
///
///   before  RTA = Σ(non-debt balances) − Σ(Available across ALL envelopes)
///               = 10,000 − (อาหาร 3,000 + จ่ายบัตร KBank 0)
///               = 7,000
///
///   create a −500 row on เงินสด (Cash) categorised to จ่ายบัตร KBank:
///     · จ่ายบัตร KBank = assigned 0 − categorised-on-KBank 0 − uncat-inflow-on-KBank 0
///                      = 0            (the row is on เงินสด, not on the card)
///     · อาหาร          = 3,000        (the row does not carry อาหาร)
///     · เงินสด balance  = 10,000 − 500 = 9,500
///
///   after   RTA = 9,500 − (3,000 + 0) = 6,500
///
/// ฿500 vanished: no envelope records it, and no error was raised. Both handlers must
/// refuse the category instead, which keeps RTA at 7,000.
/// </summary>
public class CategorisingToAPaymentEnvelopeTests
{
    private static readonly DateOnly D = new(2026, 1, 15);

    private sealed record World(
        HandlerTestFixture Fx, Guid CashId, Guid CardId, Guid FoodId, Guid PaymentEnvelopeId);

    /// <summary>
    /// The reviewer's trace exactly: cash 10,000 · อาหาร assigned 3,000 ·
    /// one Credit card (KBank), whose Payment envelope is provisioned the same
    /// way the app provisions it — lazily, on read (menunest-202).
    /// </summary>
    private static World Seed()
    {
        var fx = new HandlerTestFixture();          // Clock is 2026-01-01 UTC
        var cash = BudgetAccount.Create(fx.Family.Id, "เงินสด", BudgetAccountType.Cash, 10_000m, 0);
        var card = BudgetAccount.Create(fx.Family.Id, "KBank", BudgetAccountType.Credit, 0m, 1);
        fx.Db.BudgetAccounts.AddRange(cash, card);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "ค่ากิน", 0);
        var food = BudgetCategory.Create(fx.Family.Id, group.Id, "อาหาร", "🍜", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        fx.Db.BudgetCategories.Add(food);

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, cash.Id, null, 10_000m, D, "Opening balance", fx.User.Id));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(
            fx.Family.Id, food.Id, 2026, 1, 3_000m));
        fx.Db.SaveChanges();

        new PaymentEnvelopeProvisioner(fx.Db).EnsureForFamilyAsync(fx.Family.Id, default)
            .GetAwaiter().GetResult();
        fx.Db.SaveChanges();
        var paymentEnvelope = fx.Db.BudgetCategories.Single(c => c.PaymentForAccountId == card.Id);

        return new World(fx, cash.Id, card.Id, food.Id, paymentEnvelope.Id);
    }

    private static CreateTransactionHandler CreateHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new CreateTransactionValidator());

    private static UpdateTransactionHandler UpdateHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new UpdateTransactionValidator());

    private static async Task<MonthlySummaryDto> SummaryAsync(HandlerTestFixture fx) =>
        await new GetMonthlySummaryHandler(
                fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db),
                new PaymentEnvelopeProvisioner(fx.Db), fx.Clock)
            .Handle(new GetMonthlySummaryQuery(2026, 1, "Asia/Bangkok"), default);

    [Fact]
    public async Task The_seeded_world_starts_at_a_Ready_to_Assign_of_7000()
    {
        var w = Seed(); using var _ = w.Fx;

        var s = await SummaryAsync(w.Fx);
        // 10,000 cash − (อาหาร 3,000 + จ่ายบัตร KBank 0). The card itself is a
        // debt type and leaves the account total (menunest-203/206).
        s.ReadyToAssign.Should().Be(7_000m);
    }

    [Fact]
    public async Task Creating_a_transaction_categorised_to_a_payment_envelope_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;

        var act = async () => await CreateHandler(w.Fx).Handle(
            new CreateTransactionCommand(w.CashId, w.PaymentEnvelopeId, -500m, D, "จ่ายบัตร"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*Payment envelope*");

        // And the money is still where it was: 9,500 − 3,000 = 6,500 is the
        // number this refusal exists to prevent.
        var s = await SummaryAsync(w.Fx);
        s.ReadyToAssign.Should().Be(7_000m);
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CashId).Balance.Should().Be(10_000m);
        w.Fx.Db.BudgetTransactions.Count(t => t.CategoryId == w.PaymentEnvelopeId).Should().Be(0);
    }

    [Fact]
    public async Task Updating_a_transaction_onto_a_payment_envelope_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;

        // An ordinary, correctly categorised spend to start from.
        var created = await CreateHandler(w.Fx).Handle(
            new CreateTransactionCommand(w.CashId, w.FoodId, -500m, D, "ข้าวมันไก่"),
            CancellationToken.None);

        var act = async () => await UpdateHandler(w.Fx).Handle(
            new UpdateTransactionCommand(created.Id, w.CashId, w.PaymentEnvelopeId, -500m, D, "ข้าวมันไก่"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*Payment envelope*");

        // The row keeps its old, legal category — a refused edit changes nothing.
        w.Fx.Db.BudgetTransactions.Single(t => t.Id == created.Id).CategoryId.Should().Be(w.FoodId);
        // RTA after the legal spend: cash 9,500 − (อาหาร 3,000 − 500 = 2,500) = 7,000.
        // Unchanged, which is the invariant: a categorised spend never moves it.
        var s = await SummaryAsync(w.Fx);
        s.ReadyToAssign.Should().Be(7_000m);
    }

    [Fact]
    public async Task An_ordinary_envelope_is_still_accepted_by_create_and_by_update()
    {
        var w = Seed(); using var _ = w.Fx;

        var created = await CreateHandler(w.Fx).Handle(
            new CreateTransactionCommand(w.CashId, w.FoodId, -500m, D, "ข้าวมันไก่"),
            CancellationToken.None);
        created.CategoryId.Should().Be(w.FoodId);

        var updated = await UpdateHandler(w.Fx).Handle(
            new UpdateTransactionCommand(created.Id, w.CashId, w.FoodId, -700m, D, "ข้าวมันไก่"),
            CancellationToken.None);
        updated.CategoryId.Should().Be(w.FoodId);
        updated.Amount.Should().Be(-700m);

        // cash 10,000 − 700 = 9,300; อาหาร 3,000 − 700 = 2,300; RTA = 7,000.
        var s = await SummaryAsync(w.Fx);
        s.ReadyToAssign.Should().Be(7_000m);
    }
}
