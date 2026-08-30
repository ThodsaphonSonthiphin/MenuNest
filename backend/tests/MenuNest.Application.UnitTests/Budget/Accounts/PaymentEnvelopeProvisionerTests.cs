using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
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

    /// <summary>
    /// Guards the fix for the reviewer's concurrency finding: two callers
    /// racing to provision the SAME account's envelope must not surface a
    /// DbUpdateException as an unhandled 500 — the loser should quietly find
    /// itself with nothing left to do.
    ///
    /// Uses the real SQLite provider (not <see cref="HandlerTestFixture"/>'s
    /// InMemory context) because only a relational provider enforces the
    /// filtered unique index on PaymentForAccountId at all — InMemory ignores
    /// unique indexes entirely (see SqliteAppDbContext's own docstring).
    ///
    /// What this proves: two separate DbContext instances (db1, db2) over the
    /// SAME open SQLite connection both read the "no envelope yet" state
    /// before either commits — a genuine interleaving, not a simulated one —
    /// then db1 commits first and db2's EnsureForFamilyAndSaveAsync, called
    /// exactly as CreateAccountHandler calls it, does not throw, returns 0,
    /// and the database ends up with exactly one envelope for the account.
    ///
    /// What it does NOT prove: that the change tracker inside the LOSING
    /// context (db2) is left perfectly clean afterward. To force the two
    /// contexts to read identical pre-race state without real OS-level
    /// threads, this test stages db2's race entry with the non-saving
    /// EnsureForFamilyAsync BEFORE db1 commits, then calls
    /// EnsureForFamilyAndSaveAsync on db2 afterward — so the entities that
    /// trip the unique-index violation were staged by db2's PRIOR call, not
    /// by the one under test, and the catch block (which only discards what
    /// the CURRENT call staged) does not detach them from db2's tracker. This
    /// never happens in real production usage — EnsureForFamilyAndSaveAsync
    /// is called exactly once per (request-scoped) DbContext — so it is not a
    /// gap the fix needs to close. What it does NOT threaten is the
    /// database: SaveChangesAsync is one atomic transaction, so the failed
    /// attempt writes nothing at all, regardless of what stays tracked
    /// in-memory afterward — which is exactly what the assertions below
    /// check.
    /// </summary>
    [Fact]
    public async Task Losing_the_race_for_the_same_account_does_not_throw_or_duplicate()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options;

        using var seedDb = new SqliteAppDbContext(options);
        seedDb.Database.EnsureCreated();
        var user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        var family = Family.CreateNew("Test Family", user.Id);
        seedDb.Users.Add(user);
        seedDb.Families.Add(family);
        var acc = BudgetAccount.Create(family.Id, "KBank", BudgetAccountType.Credit, 0m, 0);
        seedDb.BudgetAccounts.Add(acc);
        await seedDb.SaveChangesAsync();

        using var db1 = new SqliteAppDbContext(options);
        using var db2 = new SqliteAppDbContext(options);
        var p1 = new PaymentEnvelopeProvisioner(db1);
        var p2 = new PaymentEnvelopeProvisioner(db2);

        // Both read the same pre-race state — neither has committed yet.
        (await p1.EnsureForFamilyAsync(family.Id, default)).Should().Be(1);
        (await p2.EnsureForFamilyAsync(family.Id, default)).Should().Be(1);

        // db1 wins: commits its envelope + group first.
        await db1.SaveChangesAsync();

        // db2 loses: its pending insert for the SAME account now collides
        // with db1's. Assert through the production entry point exactly as
        // CreateAccountHandler calls it.
        Func<Task<int>> losingCall = () => p2.EnsureForFamilyAndSaveAsync(family.Id, default);
        var result2 = await losingCall.Should().NotThrowAsync();
        result2.Subject.Should().Be(0);

        (await seedDb.BudgetCategories.CountAsync(c => c.PaymentForAccountId == acc.Id)).Should().Be(1);
    }
}
