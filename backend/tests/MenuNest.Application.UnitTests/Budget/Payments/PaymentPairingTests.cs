using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Application.UseCases.Budget.Payments.DeletePayment;
using MenuNest.Application.UseCases.Budget.Payments.MakePayment;
using MenuNest.Application.UseCases.Budget.Payments.UpdatePayment;
using MenuNest.Application.UseCases.Budget.Transactions.DeleteTransaction;
using MenuNest.Application.UseCases.Budget.Transactions.ListTransactions;
using MenuNest.Application.UseCases.Budget.Transactions.UpdateTransaction;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Payments;

/// <summary>
/// menunest-209 + R-3 (menunest-214 correction): a payment is ONE row to the
/// user — editing or deleting it must move/remove BOTH legs together, and the
/// single-leg side doors on DeleteTransactionHandler / UpdateTransactionHandler
/// must be closed. UpdatePaymentCommand carries CategoryId and enforces the
/// exact same three rules as MakePaymentCommand (R-3) — an edit must never be
/// able to drop the category off a Loan's outflow leg.
/// </summary>
public class PaymentPairingTests
{
    private static readonly DateOnly D = new(2026, 1, 15);

    private static MakePaymentHandler MakeHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new MakePaymentValidator(), fx.Clock);

    private static UpdatePaymentHandler UpdateHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new UpdatePaymentValidator());

    private static DeletePaymentHandler DeleteHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    private static GetMonthlySummaryHandler SummaryHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db),
            new PaymentEnvelopeProvisioner(fx.Db), fx.Clock);

    private static ListTransactionsHandler ListHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    private static async Task<MonthlySummaryDto> SummaryAsync(World w) =>
        await SummaryHandler(w.Fx).Handle(new GetMonthlySummaryQuery(2026, 1, "Asia/Bangkok"), default);

    private sealed record World(HandlerTestFixture Fx, Guid CashId, Guid CardId, Guid FoodId);

    private static World Seed()
    {
        var fx = new HandlerTestFixture();
        var cash = BudgetAccount.Create(fx.Family.Id, "เงินสด", BudgetAccountType.Cash, 0m, 0);
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
        return new World(fx, cash.Id, card.Id, food.Id);
    }

    private static void AddTx(World w, Guid accountId, Guid? categoryId, decimal amount)
    {
        w.Fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            w.Fx.Family.Id, accountId, categoryId, amount, D, null, w.Fx.User.Id));
        w.Fx.Db.SaveChanges();
    }

    private static async Task<PaymentDto> MakePayment(World w, decimal amount) =>
        await MakeHandler(w.Fx).Handle(
            new MakePaymentCommand(w.CashId, w.CardId, amount, D, null, "Asia/Bangkok"), default);

    // ---------- Delete ----------

    [Fact]
    public async Task Deleting_a_payment_removes_both_legs()
    {
        var w = Seed(); using var _ = w.Fx;
        var p = await MakePayment(w, 500m);

        await DeleteHandler(w.Fx).Handle(new DeletePaymentCommand(p.PaymentId), default);

        w.Fx.Db.BudgetTransactions.Count(t => t.PaymentId == p.PaymentId).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_a_payment_restores_the_payment_envelope()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var p = await MakePayment(w, 500m);
        (await SummaryAsync(w)).Groups.SelectMany(g => g.Categories)
            .Single(e => e.PaymentForAccountId == w.CardId).Available.Should().Be(0m);

        await DeleteHandler(w.Fx).Handle(new DeletePaymentCommand(p.PaymentId), default);

        (await SummaryAsync(w)).Groups.SelectMany(g => g.Categories)
            .Single(e => e.PaymentForAccountId == w.CardId).Available.Should().Be(500m);
    }

    [Fact]
    public async Task Deleting_a_nonexistent_payment_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var act = async () => await DeleteHandler(w.Fx).Handle(
            new DeletePaymentCommand(Guid.NewGuid()), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*not found*");
    }

    // menunest-209: reaching a single half is exactly the state that leaves the
    // budget silently wrong.
    [Fact]
    public async Task Deleting_ONE_leg_through_the_transaction_handler_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var p = await MakePayment(w, 500m);
        var legId = w.Fx.Db.BudgetTransactions.First(t => t.PaymentId == p.PaymentId).Id;

        var act = async () => await new DeleteTransactionHandler(w.Fx.Db, w.Fx.UserProvisioner.Object)
            .Handle(new DeleteTransactionCommand(legId), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*payment*");

        // Nothing must have been touched.
        w.Fx.Db.BudgetTransactions.Count(t => t.PaymentId == p.PaymentId).Should().Be(2);
    }

    [Fact]
    public async Task Editing_ONE_leg_through_the_transaction_handler_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var p = await MakePayment(w, 500m);
        var leg = w.Fx.Db.BudgetTransactions.First(t => t.PaymentId == p.PaymentId);
        var originalAmount = leg.Amount;

        var act = async () => await new UpdateTransactionHandler(
                w.Fx.Db, w.Fx.UserProvisioner.Object, new UpdateTransactionValidator())
            .Handle(new UpdateTransactionCommand(leg.Id, leg.AccountId, leg.CategoryId, -999m, D, null), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*payment*");

        // Symmetric with the delete twin: nothing must have been touched.
        w.Fx.Db.BudgetTransactions.Single(t => t.Id == leg.Id).Amount.Should().Be(originalAmount);
    }

    // ---------- Update ----------

    [Fact]
    public async Task Editing_a_payment_moves_both_legs_together()
    {
        var w = Seed(); using var _ = w.Fx;
        var p = await MakePayment(w, 500m);

        await UpdateHandler(w.Fx).Handle(new UpdatePaymentCommand(
            p.PaymentId, w.CashId, w.CardId, 300m, new DateOnly(2026, 1, 25), "แก้ยอด"), default);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == p.PaymentId).ToList();
        legs.Single(l => l.AccountId == w.CashId).Amount.Should().Be(-300m);
        legs.Single(l => l.AccountId == w.CardId).Amount.Should().Be(300m);
        legs.Should().OnlyContain(l => l.Date == new DateOnly(2026, 1, 25));
        legs.Should().OnlyContain(l => l.Notes == "แก้ยอด");

        // Balance reflects the NEW amount only — no half-applied delta.
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CardId).Balance.Should().Be(300m);
    }

    [Fact]
    public async Task Editing_a_loan_payment_preserves_its_category_and_leaves_RTA_unchanged()
    {
        var w = SeedLoan(); using var _ = w.Fx;

        var dto = await MakeHandler(w.Fx).Handle(
            new MakePaymentCommand(w.CashId, w.LoanId, 8_000m, D, null, "Asia/Bangkok", w.LoanEnvelopeId), default);

        var before = await SummaryHandler(w.Fx).Handle(new GetMonthlySummaryQuery(2026, 1, "Asia/Bangkok"), default);

        await UpdateHandler(w.Fx).Handle(new UpdatePaymentCommand(
            dto.PaymentId, w.CashId, w.LoanId, 6_000m, new DateOnly(2026, 1, 20), "งวดใหม่", w.LoanEnvelopeId), default);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == dto.PaymentId).ToList();
        var outLeg = legs.Single(l => l.AccountId == w.CashId);
        var inLeg = legs.Single(l => l.AccountId == w.LoanId);
        outLeg.Amount.Should().Be(-6_000m);
        outLeg.CategoryId.Should().Be(w.LoanEnvelopeId);
        inLeg.Amount.Should().Be(6_000m);
        inLeg.CategoryId.Should().BeNull();

        var after = await SummaryHandler(w.Fx).Handle(new GetMonthlySummaryQuery(2026, 1, "Asia/Bangkok"), default);
        after.ReadyToAssign.Should().Be(before.ReadyToAssign);
        // Envelope now reflects the smaller instalment: 8,000 assigned − 6,000 spent.
        after.Groups.SelectMany(g => g.Categories).Single(e => e.CategoryId == w.LoanEnvelopeId)
            .Available.Should().Be(2_000m);
    }

    // menunest-209 review: UpdatePaymentHandler must validate BEFORE it mutates
    // any balance, exactly like MakePaymentHandler — nothing may leave a
    // tracked BudgetAccount holding a reversed-but-never-reapplied delta on
    // the throw path. Asserting balances are untouched here is what would
    // catch a regression back to reverse-then-validate.
    [Fact]
    public async Task Editing_a_loan_payment_to_drop_its_category_is_refused()
    {
        var w = SeedLoan(); using var _ = w.Fx;
        var dto = await MakeHandler(w.Fx).Handle(
            new MakePaymentCommand(w.CashId, w.LoanId, 8_000m, D, null, "Asia/Bangkok", w.LoanEnvelopeId), default);
        var cashBefore = w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CashId).Balance;
        var loanBefore = w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.LoanId).Balance;

        var act = async () => await UpdateHandler(w.Fx).Handle(new UpdatePaymentCommand(
            dto.PaymentId, w.CashId, w.LoanId, 8_000m, D, null, CategoryId: null), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Envelope*");

        // Nothing must have been touched.
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CashId).Balance.Should().Be(cashBefore);
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.LoanId).Balance.Should().Be(loanBefore);
    }

    [Fact]
    public async Task Editing_a_card_payment_to_add_a_category_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var p = await MakePayment(w, 500m);
        var cashBefore = w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CashId).Balance;
        var cardBefore = w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CardId).Balance;

        var act = async () => await UpdateHandler(w.Fx).Handle(new UpdatePaymentCommand(
            p.PaymentId, w.CashId, w.CardId, 500m, D, null, CategoryId: w.FoodId), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*cannot be categorised*");

        // Nothing must have been touched.
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CashId).Balance.Should().Be(cashBefore);
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CardId).Balance.Should().Be(cardBefore);
    }

    [Fact]
    public async Task Editing_a_nonexistent_payment_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var act = async () => await UpdateHandler(w.Fx).Handle(new UpdatePaymentCommand(
            Guid.NewGuid(), w.CashId, w.CardId, 500m, D, null), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*not found*");
    }

    // ---------- Update: moving accounts (menunest-209 review: "the riskiest
    // code in the handler" — every other Update test passes back the SAME
    // from/to the payment already had, so oldFromAcc/oldToAcc are never
    // observed diverging from from/to without these) ----------

    [Fact]
    public async Task Editing_a_payment_to_a_different_paying_account_moves_the_outflow_leg()
    {
        var w = Seed(); using var _ = w.Fx;
        var cash2 = BudgetAccount.Create(w.Fx.Family.Id, "ธนาคาร", BudgetAccountType.Cash, 2_000m, 2);
        w.Fx.Db.BudgetAccounts.Add(cash2);
        await w.Fx.Db.SaveChangesAsync();

        var cashBefore = w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CashId).Balance; // 10,000
        var p = await MakePayment(w, 500m); // Cash -> Card

        // Move the SAME payment off Cash onto Cash2, and change the amount too.
        await UpdateHandler(w.Fx).Handle(new UpdatePaymentCommand(
            p.PaymentId, cash2.Id, w.CardId, 700m, D, null), default);

        // The ORIGINAL Cash account is back to its pre-payment balance — the
        // outflow leg moved off it entirely, it did not just net to zero delta.
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CashId).Balance.Should().Be(cashBefore);
        // Cash2 carries the NEW amount only (2,000 opening − 700, not −500 too).
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == cash2.Id).Balance.Should().Be(1_300m);
        // Card, unchanged as the target, carries the new amount (not old+new).
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CardId).Balance.Should().Be(700m);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == p.PaymentId).ToList();
        legs.Single(l => l.Amount < 0).AccountId.Should().Be(cash2.Id);
        legs.Single(l => l.Amount > 0).AccountId.Should().Be(w.CardId);
    }

    [Fact]
    public async Task Editing_a_payment_to_move_its_target_from_Credit_to_Loan_requires_and_applies_a_category()
    {
        var w = Seed(); using var _ = w.Fx;
        var loan = BudgetAccount.Create(w.Fx.Family.Id, "รถ", BudgetAccountType.Loan, 0m, 2);
        w.Fx.Db.BudgetAccounts.Add(loan);
        await w.Fx.Db.SaveChangesAsync();

        var p = await MakePayment(w, 500m); // Cash -> Card, both legs uncategorised
        var before = await SummaryAsync(w);

        // Re-target the SAME payment from the Credit card to the Loan — the
        // new-target rule (Loan requires a category) must be checked against
        // the NEW `to`, not the Card the payment used to pay.
        await UpdateHandler(w.Fx).Handle(new UpdatePaymentCommand(
            p.PaymentId, w.CashId, loan.Id, 500m, D, null, CategoryId: w.FoodId), default);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == p.PaymentId).ToList();
        var outLeg = legs.Single(l => l.Amount < 0);
        var inLeg = legs.Single(l => l.Amount > 0);
        outLeg.AccountId.Should().Be(w.CashId);
        outLeg.CategoryId.Should().Be(w.FoodId);
        inLeg.AccountId.Should().Be(loan.Id);
        inLeg.CategoryId.Should().BeNull();

        // Card is back to zero — the payment no longer targets it at all.
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CardId).Balance.Should().Be(0m);
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == loan.Id).Balance.Should().Be(500m);

        // A payment never moves Ready to Assign, regardless of which debt
        // type it targets before vs. after the edit.
        var after = await SummaryAsync(w);
        after.ReadyToAssign.Should().Be(before.ReadyToAssign);
        // Food is the Loan's funding Envelope now — Available fell by 500
        // (3,000 assigned − 500 categorised), exactly like a Loan payment.
        after.Groups.SelectMany(g => g.Categories).Single(e => e.CategoryId == w.FoodId)
            .Available.Should().Be(2_500m);
    }

    [Fact]
    public async Task Editing_a_payment_to_move_its_target_to_Loan_without_a_category_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var loan = BudgetAccount.Create(w.Fx.Family.Id, "รถ", BudgetAccountType.Loan, 0m, 2);
        w.Fx.Db.BudgetAccounts.Add(loan);
        await w.Fx.Db.SaveChangesAsync();
        var p = await MakePayment(w, 500m); // Cash -> Card, uncategorised

        var act = async () => await UpdateHandler(w.Fx).Handle(new UpdatePaymentCommand(
            p.PaymentId, w.CashId, loan.Id, 500m, D, null, CategoryId: null), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Envelope*");

        // The payment must still be sitting exactly where it was.
        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == p.PaymentId).ToList();
        legs.Single(l => l.Amount < 0).AccountId.Should().Be(w.CashId);
        legs.Single(l => l.Amount > 0).AccountId.Should().Be(w.CardId);
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CardId).Balance.Should().Be(500m);
        w.Fx.Db.BudgetAccounts.Single(a => a.Id == loan.Id).Balance.Should().Be(0m);
    }

    // ---------- PaymentId visibility (R-4: the field a client needs to find
    // and act on the OTHER leg of a payment — without it, nothing can call
    // PUT/DELETE /api/budget/payments/{paymentId} for a payment that outlives
    // the call that created it) ----------

    [Fact]
    public async Task Listing_transactions_exposes_a_shared_nonnull_PaymentId_on_both_legs()
    {
        var w = Seed(); using var _ = w.Fx;
        var ordinary = BudgetTransaction.Create(w.Fx.Family.Id, w.CashId, w.FoodId, -100m, D, null, w.Fx.User.Id);
        w.Fx.Db.BudgetTransactions.Add(ordinary);
        await w.Fx.Db.SaveChangesAsync();

        var p = await MakePayment(w, 500m);

        var rows = await ListHandler(w.Fx).Handle(new ListTransactionsQuery(2026, 1, null), default);

        var paymentRows = rows.Where(r => r.PaymentId == p.PaymentId).ToList();
        paymentRows.Should().HaveCount(2);
        paymentRows.Select(r => r.AccountId).Should().BeEquivalentTo(new[] { w.CashId, w.CardId });

        rows.Single(r => r.Id == ordinary.Id).PaymentId.Should().BeNull();
    }

    // ---------- Loan seed (shared with MakePaymentHandlerTests' shape) ----------

    private sealed record LoanWorld(HandlerTestFixture Fx, Guid CashId, Guid LoanId, Guid LoanEnvelopeId);

    private static LoanWorld SeedLoan()
    {
        var fx = new HandlerTestFixture();
        var cash = BudgetAccount.Create(fx.Family.Id, "เงินสด", BudgetAccountType.Cash, 0m, 0);
        var loan = BudgetAccount.Create(fx.Family.Id, "รถ", BudgetAccountType.Loan, 0m, 1);
        fx.Db.BudgetAccounts.AddRange(cash, loan);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "หนี้สิน", 0);
        var envelope = BudgetCategory.Create(fx.Family.Id, group.Id, "ผ่อนรถ", "🚗", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        fx.Db.BudgetCategories.Add(envelope);

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, cash.Id, null, 100_000m, D, "Opening balance", fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, loan.Id, null, -300_000m, D, "Opening balance", fx.User.Id));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(
            fx.Family.Id, envelope.Id, 2026, 1, 8_000m));
        fx.Db.SaveChanges();
        return new LoanWorld(fx, cash.Id, loan.Id, envelope.Id);
    }

    // ---------- Regression: an ordinary transaction is unaffected ----------

    [Fact]
    public async Task An_ordinary_transaction_still_deletes_and_edits_normally()
    {
        var w = Seed(); using var _ = w.Fx;
        var acc = w.Fx.Db.BudgetAccounts.Single(a => a.Id == w.CashId);
        var tx = BudgetTransaction.Create(w.Fx.Family.Id, w.CashId, w.FoodId, -200m, D, "ก๋วยเตี๋ยว", w.Fx.User.Id);
        w.Fx.Db.BudgetTransactions.Add(tx);
        acc.AdjustBalance(-200m);
        await w.Fx.Db.SaveChangesAsync();

        // Edit still works (PaymentId is null).
        await new UpdateTransactionHandler(w.Fx.Db, w.Fx.UserProvisioner.Object, new UpdateTransactionValidator())
            .Handle(new UpdateTransactionCommand(tx.Id, w.CashId, w.FoodId, -250m, D, "แก้ไข"), default);
        w.Fx.Db.BudgetTransactions.Single(t => t.Id == tx.Id).Amount.Should().Be(-250m);

        // Delete still works.
        await new DeleteTransactionHandler(w.Fx.Db, w.Fx.UserProvisioner.Object)
            .Handle(new DeleteTransactionCommand(tx.Id), default);
        w.Fx.Db.BudgetTransactions.Any(t => t.Id == tx.Id).Should().BeFalse();
    }
}
