using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class PaymentEnvelopeProvisionerTests
{
    private static BudgetAccount AddAccount(HandlerTestFixture fx, string name, BudgetAccountType type)
    {
        var acc = BudgetAccount.Create(fx.Family.Id, name, type, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        return acc;
    }

    [Fact]
    public async Task A_credit_account_gets_one_payment_envelope_in_the_credit_group()
    {
        using var fx = new HandlerTestFixture();
        var acc = AddAccount(fx, "KBank", BudgetAccountType.Credit);
        await fx.Db.SaveChangesAsync();

        await new PaymentEnvelopeProvisioner(fx.Db).EnsureForFamilyAsync(fx.Family.Id, default);
        await fx.Db.SaveChangesAsync();

        var env = await fx.Db.BudgetCategories.SingleAsync(c => c.PaymentForAccountId == acc.Id);
        env.Name.Should().Be("จ่ายบัตร KBank");
        var group = await fx.Db.BudgetCategoryGroups.SingleAsync(g => g.Id == env.GroupId);
        group.Name.Should().Be("บัตรเครดิต");
    }

    [Fact]
    public async Task A_loan_account_gets_none()
    {
        using var fx = new HandlerTestFixture();
        AddAccount(fx, "รถ", BudgetAccountType.Loan);
        AddAccount(fx, "เงินสด", BudgetAccountType.Cash);
        await fx.Db.SaveChangesAsync();

        await new PaymentEnvelopeProvisioner(fx.Db).EnsureForFamilyAsync(fx.Family.Id, default);
        await fx.Db.SaveChangesAsync();

        (await fx.Db.BudgetCategories.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Running_it_twice_creates_nothing_the_second_time()
    {
        using var fx = new HandlerTestFixture();
        AddAccount(fx, "KBank", BudgetAccountType.Credit);
        await fx.Db.SaveChangesAsync();
        var sut = new PaymentEnvelopeProvisioner(fx.Db);

        (await sut.EnsureForFamilyAsync(fx.Family.Id, default)).Should().Be(1);
        await fx.Db.SaveChangesAsync();
        (await sut.EnsureForFamilyAsync(fx.Family.Id, default)).Should().Be(0);
        await fx.Db.SaveChangesAsync();

        (await fx.Db.BudgetCategories.CountAsync()).Should().Be(1);
        (await fx.Db.BudgetCategoryGroups.CountAsync(g => g.Name == "บัตรเครดิต")).Should().Be(1);
    }

    [Fact]
    public async Task Two_cards_get_two_envelopes_in_one_shared_group()
    {
        using var fx = new HandlerTestFixture();
        AddAccount(fx, "KBank", BudgetAccountType.Credit);
        AddAccount(fx, "SCB", BudgetAccountType.Credit);
        await fx.Db.SaveChangesAsync();

        await new PaymentEnvelopeProvisioner(fx.Db).EnsureForFamilyAsync(fx.Family.Id, default);
        await fx.Db.SaveChangesAsync();

        var envs = await fx.Db.BudgetCategories.ToListAsync();
        envs.Should().HaveCount(2);
        envs.Select(e => e.GroupId).Distinct().Should().HaveCount(1);
        envs.Select(e => e.Name).Should().BeEquivalentTo("จ่ายบัตร KBank", "จ่ายบัตร SCB");
    }

    /// <summary>
    /// Guards the ordering correction to the brief's Step 4: the provisioner's
    /// EnsureForFamilyAsync queries the database with LINQ, so it must run
    /// AFTER the account is saved — not before, as the brief originally had it.
    /// Calling through CreateAccountHandler end-to-end (rather than driving the
    /// provisioner directly, as the tests above do) is the only way this would
    /// have caught the wrong ordering: the brief's own directly-driven tests
    /// save the account before calling the provisioner regardless of what
    /// order CreateAccountHandler itself uses internally.
    /// </summary>
    [Fact]
    public async Task Creating_a_credit_account_through_the_handler_gives_it_a_payment_envelope()
    {
        using var fx = new HandlerTestFixture();
        var handler = new CreateAccountHandler(
            fx.Db, fx.UserProvisioner.Object, new CreateAccountValidator(), fx.Clock,
            new PaymentEnvelopeProvisioner(fx.Db));

        var result = await handler.Handle(
            new CreateAccountCommand("KBank", BudgetAccountType.Credit, OpeningBalance: 0m, TimeZoneId: null),
            CancellationToken.None);

        var env = await fx.Db.BudgetCategories.SingleOrDefaultAsync(c => c.PaymentForAccountId == result.Id);
        env.Should().NotBeNull("a Credit account must get its Payment envelope in the same unit of work");
        env!.Name.Should().Be("จ่ายบัตร KBank");
    }
}
