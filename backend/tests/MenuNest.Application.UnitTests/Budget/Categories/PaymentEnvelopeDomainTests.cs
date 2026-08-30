using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Categories;

public class PaymentEnvelopeDomainTests
{
    private static BudgetCategory NewPaymentEnvelope() =>
        BudgetCategory.CreatePaymentEnvelope(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "KBank", 0);

    private static BudgetCategory NewOrdinary() =>
        BudgetCategory.Create(Guid.NewGuid(), Guid.NewGuid(), "อาหาร", "🍜", 0);

    [Fact]
    public void A_payment_envelope_is_named_for_its_account()
    {
        NewPaymentEnvelope().Name.Should().Be("จ่ายบัตร KBank");
    }

    [Fact]
    public void A_payment_envelope_knows_it_is_one()
    {
        NewPaymentEnvelope().IsPaymentEnvelope.Should().BeTrue();
        NewOrdinary().IsPaymentEnvelope.Should().BeFalse();
    }

    // menunest-205: the Daily allowance divides Everyday money by days left in
    // the month, so a payment envelope in that pot would RAISE "spend this much
    // today" every time the card is used.
    [Fact]
    public void A_payment_envelope_cannot_be_marked_everyday()
    {
        var env = NewPaymentEnvelope();
        var act = () => env.MarkEveryday(true);
        act.Should().Throw<DomainException>().WithMessage("*everyday*");
    }

    [Fact]
    public void Unmarking_everyday_on_a_payment_envelope_is_a_harmless_no_op()
    {
        var env = NewPaymentEnvelope();
        env.MarkEveryday(false);
        env.IsEveryday.Should().BeFalse();
    }

    [Fact]
    public void A_payment_envelope_cannot_be_renamed_or_regrouped_by_Update()
    {
        var env = NewPaymentEnvelope();
        var act = () => env.Update("บัตรแม่", null, Guid.NewGuid(), 3);
        act.Should().Throw<DomainException>().WithMessage("*payment envelope*");
    }

    [Fact]
    public void A_payment_envelope_cannot_be_hidden_by_hand()
    {
        var act = () => NewPaymentEnvelope().Hide();
        act.Should().Throw<DomainException>().WithMessage("*payment envelope*");
    }

    // The name follows the Account (menunest-212), so an account rename must be
    // able to push through — by its own method, not by Update.
    [Fact]
    public void RenameForAccount_retitles_the_envelope()
    {
        var env = NewPaymentEnvelope();
        env.RenameForAccount("KBank Platinum");
        env.Name.Should().Be("จ่ายบัตร KBank Platinum");
    }

    [Fact]
    public void An_ordinary_envelope_is_unaffected_by_any_of_these_guards()
    {
        var cat = NewOrdinary();
        cat.MarkEveryday(true);
        cat.Update("อาหาร2", "🍲", cat.GroupId, 2);
        cat.Hide();
        cat.IsEveryday.Should().BeTrue();
        cat.Name.Should().Be("อาหาร2");
        cat.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void Both_legs_of_a_payment_carry_the_same_PaymentId()
    {
        var famId = Guid.NewGuid();
        var payId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 30);

        var outLeg = BudgetTransaction.CreatePaymentLeg(famId, Guid.NewGuid(), null, -500m, date, null, userId, payId);
        var inLeg = BudgetTransaction.CreatePaymentLeg(famId, Guid.NewGuid(), null, 500m, date, null, userId, payId);

        outLeg.PaymentId.Should().Be(payId);
        inLeg.PaymentId.Should().Be(payId);
        // The "a payment is not spending" invariant that used to live here
        // moved out of this factory when it gained categoryId (menunest-214) —
        // it is now MakePaymentHandler's job to pass null for the in-leg
        // unconditionally. All this checks now is that CreatePaymentLeg
        // passes through whatever CategoryId its caller gave it.
        outLeg.CategoryId.Should().BeNull("this call passed null");
        inLeg.CategoryId.Should().BeNull("this call passed null");
    }

    // menunest-214: CreatePaymentLeg itself enforces nothing about CategoryId —
    // it is a plain pass-through. The business rule (required for a Loan,
    // refused for a Credit card, and refused for another debt's Payment
    // envelope) lives entirely in MakePaymentHandler, covered in
    // MakePaymentHandlerTests.
    [Fact]
    public void CreatePaymentLeg_passes_a_non_null_categoryId_through_unchanged()
    {
        var categoryId = Guid.NewGuid();
        var leg = BudgetTransaction.CreatePaymentLeg(
            Guid.NewGuid(), Guid.NewGuid(), categoryId, -8_000m,
            new DateOnly(2026, 8, 30), null, Guid.NewGuid(), Guid.NewGuid());

        leg.CategoryId.Should().Be(categoryId);
    }

    [Fact]
    public void An_ordinary_transaction_has_no_PaymentId()
    {
        BudgetTransaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, -100m,
            new DateOnly(2026, 8, 30), null, Guid.NewGuid())
            .PaymentId.Should().BeNull();
    }
}
