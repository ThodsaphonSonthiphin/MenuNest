using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Accounts.DeleteAccount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

/// <summary>
/// Deleting a Credit account, on a RELATIONAL context.
///
/// menunest-202 gave every Credit account a Payment envelope, and
/// <c>BudgetCategoryConfiguration</c> binds it with
/// <c>HasForeignKey(x =&gt; x.PaymentForAccountId).OnDelete(DeleteBehavior.Restrict)</c>.
/// <see cref="DeleteAccountHandler"/> checks only for transactions, so an UNUSED
/// card — zero transactions, one envelope — passes every check in the handler and
/// then dies at the database with an unhandled <c>DbUpdateException</c>: HTTP 500,
/// "An unexpected error occurred." on screen. Deleting an unused card worked
/// before this branch, so that is a regression.
///
/// The InMemory provider enforces no foreign keys at all, which is exactly why
/// <see cref="DeleteAccountHandlerTests"/> (InMemory) cannot see this and why
/// these tests are relational. menunest-210's claim that "an unused one has an
/// empty Payment envelope, so both go together harmlessly" was the reasoning
/// this file disproves; the ADR now records what actually happens.
/// </summary>
public sealed class DeleteAccountRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;
    private readonly Family _family;

    public DeleteAccountRelationalTests()
    {
        // `Foreign Keys=True` is load-bearing: SQLite only enforces foreign keys
        // when the pragma is on, and without it this file would prove nothing —
        // the delete would silently ORPHAN the envelope instead of being
        // Restricted, which is not what SQL Server does in production.
        _conn = new SqliteConnection("Filename=:memory:;Foreign Keys=True");
        _conn.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options;
        _db = new SqliteAppDbContext(options);
        _db.Database.EnsureCreated();

        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _family = Family.CreateNew("Test Family", _user.Id);
        _user.JoinFamily(_family.Id);
        _db.Users.Add(_user);
        _db.Families.Add(_family);
        _db.SaveChanges();
    }

    /// <summary>
    /// Detaches everything this test seeded, so the handler runs against the
    /// SAME empty change tracker a real request gets.
    ///
    /// This is load-bearing, not hygiene. With the Payment envelope TRACKED,
    /// EF severs the relationship in memory (it writes
    /// <c>PaymentForAccountId = NULL</c>) and the delete quietly succeeds,
    /// leaving an ordinary envelope still called "จ่ายบัตร KBank" behind.
    /// A real request never has it tracked — <see cref="DeleteAccountHandler"/>
    /// loads only the account — so EF issues the bare DELETE and the database
    /// raises the FK violation. Seeding and asserting in one context would
    /// therefore hide the very defect under test.
    /// </summary>
    private void DetachAll() => _db.ChangeTracker.Clear();

    private DeleteAccountHandler NewHandler()
    {
        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.RequireFamilyAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync((_user, _family.Id));
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(_user);
        return new DeleteAccountHandler(_db, users.Object);
    }

    /// <summary>A Credit account with its Payment envelope, provisioned the way the app does.</summary>
    private async Task<(BudgetAccount Account, BudgetCategory Envelope)> SeedCardAsync(string name = "KBank")
    {
        var acc = BudgetAccount.Create(_family.Id, name, BudgetAccountType.Credit, 0m, 0);
        _db.BudgetAccounts.Add(acc);
        await _db.SaveChangesAsync();
        await new PaymentEnvelopeProvisioner(_db).EnsureForFamilyAsync(_family.Id, default);
        await _db.SaveChangesAsync();
        var env = await _db.BudgetCategories.SingleAsync(c => c.PaymentForAccountId == acc.Id);
        return (acc, env);
    }

    [Fact]
    public async Task Deleting_an_unused_credit_account_takes_its_payment_envelope_with_it()
    {
        var (acc, env) = await SeedCardAsync();

        DetachAll();
        await NewHandler().Handle(new DeleteAccountCommand(acc.Id), CancellationToken.None);

        (await _db.BudgetAccounts.AnyAsync(a => a.Id == acc.Id)).Should().BeFalse();
        (await _db.BudgetCategories.AnyAsync(c => c.Id == env.Id)).Should().BeFalse(
            "the envelope holds the FK that would otherwise Restrict the delete");
    }

    [Fact]
    public async Task Deleting_an_unused_credit_account_takes_its_envelope_assignments_too()
    {
        var (acc, env) = await SeedCardAsync();
        // MonthlyAssignment → BudgetCategory is Restrict as well, so the
        // assignments have to go in the same unit of work or the envelope
        // delete fails for a second, different reason.
        _db.MonthlyAssignments.Add(MonthlyAssignment.Create(_family.Id, env.Id, 2026, 1, 2_000m));
        await _db.SaveChangesAsync();

        DetachAll();
        await NewHandler().Handle(new DeleteAccountCommand(acc.Id), CancellationToken.None);

        (await _db.MonthlyAssignments.AnyAsync(a => a.CategoryId == env.Id)).Should().BeFalse();
        (await _db.BudgetCategories.AnyAsync(c => c.Id == env.Id)).Should().BeFalse();
        (await _db.BudgetAccounts.AnyAsync(a => a.Id == acc.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task A_cash_account_with_no_transactions_still_deletes()
    {
        var acc = BudgetAccount.Create(_family.Id, "เงินสด", BudgetAccountType.Cash, 0m, 0);
        _db.BudgetAccounts.Add(acc);
        await _db.SaveChangesAsync();

        DetachAll();
        await NewHandler().Handle(new DeleteAccountCommand(acc.Id), CancellationToken.None);

        (await _db.BudgetAccounts.AnyAsync(a => a.Id == acc.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task A_credit_account_with_transactions_is_still_refused_and_keeps_its_envelope()
    {
        var (acc, env) = await SeedCardAsync();
        _db.BudgetTransactions.Add(BudgetTransaction.Create(
            _family.Id, acc.Id, null, -500m, new DateOnly(2026, 1, 15), null, _user.Id));
        await _db.SaveChangesAsync();

        DetachAll();
        var act = async () => await NewHandler().Handle(new DeleteAccountCommand(acc.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*close it instead*");
        (await _db.BudgetCategories.AnyAsync(c => c.Id == env.Id)).Should().BeTrue(
            "a refused delete must not have already destroyed the envelope");
        (await _db.BudgetAccounts.AnyAsync(a => a.Id == acc.Id)).Should().BeTrue();
    }

    // BudgetChange → BudgetCategory is Restrict ON PURPOSE (menunest-197: a row
    // whose Envelope was deleted must STAY on the history list, greyed, saying
    // why). Deleting the envelope out from under one would be the SAME
    // DbUpdateException-as-HTTP-500 shape this file exists to close, so it is
    // refused with a domain error the SPA can show — exactly as
    // DeleteCategoryHandler already refuses it.
    [Fact]
    public async Task A_credit_account_whose_envelope_carries_budget_history_is_refused_not_crashed()
    {
        var (acc, env) = await SeedCardAsync();
        _db.BudgetChanges.Add(BudgetChange.RecordAssign(
            _family.Id, _user.Id, 2026, 1, env.Id, 2_000m, batchId: null));
        await _db.SaveChangesAsync();

        DetachAll();
        var act = async () => await NewHandler().Handle(new DeleteAccountCommand(acc.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*history*");
        (await _db.BudgetAccounts.AnyAsync(a => a.Id == acc.Id)).Should().BeTrue();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
