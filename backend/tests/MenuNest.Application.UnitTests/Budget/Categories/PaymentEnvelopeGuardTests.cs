using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Categories.DeleteCategory;
using MenuNest.Application.UseCases.Budget.Categories.SetEverydayMarks;
using MenuNest.Application.UseCases.Budget.Groups.DeleteGroup;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UnitTests.Budget.Categories;

/// <summary>
/// menunest-205 at the handler layer: a Payment envelope is fundable but not
/// editable. The domain already refuses these (Task 1); these tests prove the
/// handlers surface that refusal too — rather than an EF failure or a silent
/// success — and that the group-delete side door is closed as well.
/// </summary>
public class PaymentEnvelopeGuardTests
{
    /// <summary>Seeds one Credit account and runs the provisioner, returning the envelope it creates.</summary>
    private static async Task<(HandlerTestFixture fx, Guid envId)> SeedCardAndEnvelope()
    {
        var fx = new HandlerTestFixture();
        var acc = BudgetAccount.Create(fx.Family.Id, "KBank", BudgetAccountType.Credit, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        await fx.Db.SaveChangesAsync();

        await new PaymentEnvelopeProvisioner(fx.Db).EnsureForFamilyAndSaveAsync(fx.Family.Id, default);

        var env = await fx.Db.BudgetCategories.SingleAsync(c => c.PaymentForAccountId == acc.Id);
        return (fx, env.Id);
    }

    private static SetEverydayMarksHandler NewSetEverydayMarksHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new SetEverydayMarksValidator(),
            new AllowanceFreezer(fx.Db), fx.Clock, new BudgetChangeRecorder(fx.Db));

    [Fact]
    public async Task Deleting_a_payment_envelope_is_refused()
    {
        var (fx, envId) = await SeedCardAndEnvelope();
        using var _ = fx;
        var act = async () => await new DeleteCategoryHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new DeleteCategoryCommand(envId), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*payment envelope*");
        fx.Db.BudgetCategories.Any(c => c.Id == envId).Should().BeTrue();
    }

    [Fact]
    public async Task Marking_a_payment_envelope_everyday_is_refused()
    {
        var (fx, envId) = await SeedCardAndEnvelope();
        using var _ = fx;
        var act = async () => await NewSetEverydayMarksHandler(fx)
            .Handle(new SetEverydayMarksCommand([new EverydayMark(envId, true)], "Asia/Bangkok"), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*everyday*");
        fx.Db.BudgetCategories.Single(c => c.Id == envId).IsEveryday.Should().BeFalse();
    }

    /// <summary>
    /// SetEverydayMarksHandler is a bulk path — a batch that mixes one ordinary
    /// envelope with the payment envelope must be refused as a WHOLE, not
    /// applied partially before hitting the offending one.
    /// </summary>
    [Fact]
    public async Task A_batch_containing_one_payment_envelope_is_refused_as_a_whole()
    {
        var (fx, envId) = await SeedCardAndEnvelope();
        using var _ = fx;
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 1);
        fx.Db.BudgetCategoryGroups.Add(group);
        var ordinary = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(ordinary);
        await fx.Db.SaveChangesAsync();

        var act = async () => await NewSetEverydayMarksHandler(fx).Handle(
            new SetEverydayMarksCommand(
                [new EverydayMark(ordinary.Id, true), new EverydayMark(envId, true)], "Asia/Bangkok"),
            default);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*everyday*");
        // Neither envelope's mark took — the ordinary one is not left flipped
        // just because it was processed before the payment envelope.
        fx.Db.BudgetCategories.Single(c => c.Id == ordinary.Id).IsEveryday.Should().BeFalse();
        fx.Db.BudgetCategories.Single(c => c.Id == envId).IsEveryday.Should().BeFalse();
        fx.Db.DailyAllowances.Should().BeEmpty("no save must have happened at all");
    }

    [Fact]
    public async Task Deleting_the_credit_group_while_it_holds_an_envelope_is_refused()
    {
        var (fx, envId) = await SeedCardAndEnvelope();
        using var _ = fx;
        var groupId = fx.Db.BudgetCategories.Single(c => c.Id == envId).GroupId;
        var act = async () => await new DeleteGroupHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new DeleteGroupCommand(groupId), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*payment envelope*");
        fx.Db.BudgetCategoryGroups.Any(g => g.Id == groupId).Should().BeTrue();
    }

    // ── Regression: the new guards must not catch ordinary envelopes/groups ──

    [Fact]
    public async Task An_ordinary_envelope_still_deletes()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        await new DeleteCategoryHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new DeleteCategoryCommand(cat.Id), default);

        fx.Db.BudgetCategories.Any(c => c.Id == cat.Id).Should().BeFalse();
    }

    [Fact]
    public async Task An_ordinary_envelope_still_accepts_an_everyday_mark()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        await NewSetEverydayMarksHandler(fx).Handle(
            new SetEverydayMarksCommand([new EverydayMark(cat.Id, true)], "Asia/Bangkok"), default);

        fx.Db.BudgetCategories.Single(c => c.Id == cat.Id).IsEveryday.Should().BeTrue();
    }

    [Fact]
    public async Task An_ordinary_group_still_deletes()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        await fx.Db.SaveChangesAsync();

        await new DeleteGroupHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new DeleteGroupCommand(group.Id), default);

        fx.Db.BudgetCategoryGroups.Any(g => g.Id == group.Id).Should().BeFalse();
    }
}
