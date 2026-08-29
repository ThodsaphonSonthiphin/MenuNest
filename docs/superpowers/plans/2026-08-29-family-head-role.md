# Family Head Role Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use sp-subagent-driven-development (recommended) or sp-executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every Family a transferable **head**, and let that head undo any member's budget change — MenuNest's first permission distinction.

**Architecture:** One nullable `HeadUserId` on `Family`, backfilled for existing rows. The head is taken deliberately, never assigned automatically: only the current head hands it over, and the head cannot leave the Family while other members remain. The role unlocks exactly one power. The undo/redo ownership check written in the previous plan widens at its single marked seam.

**Tech Stack:** .NET 10, EF Core, Mediator, FluentValidation, xUnit + Moq + FluentAssertions.

**Spec:** `docs/adr/menunest-198-*.md` (whose acts) and `docs/adr/menunest-201-*.md` (the role itself). Read both before starting.

**Depends on:** `docs/superpowers/plans/2026-08-29-budget-undo-engine.md`, Tasks 1–7, which must be merged first. This plan edits `UndoChangeHandler` and `RedoChangeHandler`, which that plan creates.

## Global Constraints

- **This plan ships almost nothing visible.** Only Task 8 (`IsHead` on the member list) reaches the UI, and it adds a field the SPA may ignore. Every commit must be safe to deploy on push — CD deploys on every push to `main`.
- **`Family` gains a column, so the entity and its EF configuration must land in the SAME commit** (CLAUDE.md). `Family` is mapped in `FamilyConfiguration.cs`.
- **The migration is applied to prod BY HAND, after deploy.** Task 9 carries the runbook.
- **`dotnet ef` needs both a PATH and a runtime hint on this machine.** The working invocation, proven in the previous plan:
  `DOTNET_ROOT=/opt/homebrew/Cellar/dotnet/10.0.400/libexec ~/.dotnet/tools/dotnet-ef …`
- **Tests use Moq, not NSubstitute.**
- **Every commit references the issue** — `(#106)` in the subject or `Refs #106` in the body.
- **`git add <explicit paths>` only.**
- The pre-commit hook runs the full suite; expect ~40s and never `--no-verify`.
- **The role unlocks exactly ONE power** (menunest-201). If a task tempts you to gate renaming the Family, rotating the invite code, or deleting anything — stop. That is a different feature.

## File Structure

| File | Responsibility |
|---|---|
| `backend/src/MenuNest.Domain/Entities/Family.cs` | `HeadUserId`, `AssignHead`, `TransferHeadTo`, `ClearHead` |
| `backend/src/MenuNest.Infrastructure/Persistence/Configurations/FamilyConfiguration.cs` | the column |
| `.../Persistence/Migrations/*_AddFamilyHead.cs` | the column **and** the backfill |
| `backend/src/MenuNest.Application/UseCases/Families/TransferHead/*` | command, validator, handler |
| `backend/src/MenuNest.Application/UseCases/Families/LeaveFamily/LeaveFamilyHandler.cs` | the guard |
| `backend/src/MenuNest.Application/UseCases/Families/JoinFamily/JoinFamilyHandler.cs` | headless Family adopts its next joiner |
| `backend/src/MenuNest.Application/Abstractions/IWebPushSender.cs` | one generic send method |
| `backend/src/MenuNest.Infrastructure/Services/WebPushSender.cs` + `NullWebPushSender.cs` | its implementation |
| `backend/src/MenuNest.Application/UseCases/Budget/History/UndoChange/UndoChangeHandler.cs` | widen the seam, notify |
| `backend/src/MenuNest.Application/UseCases/Budget/History/RedoChange/RedoChangeHandler.cs` | widen the seam |
| `backend/src/MenuNest.Application/UseCases/Families/FamilyMemberDto.cs` | `IsHead` |

---

### Task 1: `HeadUserId` on Family, its mapping, and the backfill migration

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/Family.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/FamilyConfiguration.cs`
- Create: the migration
- Test: `backend/tests/MenuNest.Application.UnitTests/Families/FamilyHeadTests.cs`

**Interfaces:**
- Produces: `Family.HeadUserId` (`Guid?`), `Family.AssignHead(Guid userId)`, `Family.TransferHeadTo(Guid currentHeadUserId, Guid newHeadUserId)`, `Family.ClearHead()`.

`HeadUserId` is **nullable** because menunest-201 rule 4 allows a Family with no head: the last member left, and the next joiner takes it.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Families;

public class FamilyHeadTests
{
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void A_new_family_has_its_creator_as_head()
    {
        var f = Family.CreateNew("Test", Creator);
        f.HeadUserId.Should().Be(Creator);
    }

    [Fact]
    public void Only_the_current_head_may_transfer_the_role()
    {
        var f = Family.CreateNew("Test", Creator);

        var act = () => f.TransferHeadTo(currentHeadUserId: Other, newHeadUserId: Other);

        act.Should().Throw<DomainException>().WithMessage("*only the family head*");
    }

    [Fact]
    public void The_head_may_hand_the_role_to_another_member()
    {
        var f = Family.CreateNew("Test", Creator);

        f.TransferHeadTo(Creator, Other);

        f.HeadUserId.Should().Be(Other);
    }

    [Fact]
    public void Transferring_to_the_current_head_is_rejected()
    {
        var f = Family.CreateNew("Test", Creator);

        var act = () => f.TransferHeadTo(Creator, Creator);

        act.Should().Throw<DomainException>().WithMessage("*already*");
    }

    [Fact]
    public void A_headless_family_adopts_the_head_it_is_assigned()
    {
        var f = Family.CreateNew("Test", Creator);
        f.ClearHead();
        f.HeadUserId.Should().BeNull();

        f.AssignHead(Other);

        f.HeadUserId.Should().Be(Other);
    }

    [Fact]
    public void Assigning_a_head_to_a_family_that_has_one_is_rejected()
    {
        var f = Family.CreateNew("Test", Creator);

        var act = () => f.AssignHead(Other);

        act.Should().Throw<DomainException>().WithMessage("*already has a head*");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~FamilyHeadTests`
Expected: FAIL to compile — `HeadUserId` does not exist.

- [ ] **Step 3: Add the property and methods to `Family`**

Add the property beside `CreatedByUserId`:

```csharp
    /// <summary>
    /// The member who may undo any other member's budget change (menunest-198).
    /// The app's ONLY permission distinction, and it unlocks exactly that one
    /// power (menunest-201).
    ///
    /// <para>Nullable because a Family can legitimately have no head: its last
    /// member left, and the next person to join takes the role.</para>
    ///
    /// <para>Deliberately NOT <see cref="CreatedByUserId"/>: that records who
    /// happened to create the Family, and LeaveFamily never clears it, so it
    /// can already point at somebody who left.</para>
    /// </summary>
    public Guid? HeadUserId { get; private set; }
```

Set it in `CreateNew`, inside the object initialiser:

```csharp
            CreatedByUserId = createdByUserId,
            HeadUserId = createdByUserId
```

And add the three methods after `RotateInviteCode`:

```csharp
    /// <summary>Hands the role on. Only the current head may do this (menunest-201).</summary>
    public void TransferHeadTo(Guid currentHeadUserId, Guid newHeadUserId)
    {
        if (HeadUserId != currentHeadUserId)
            throw new DomainException("Only the family head can hand the role over.");
        if (newHeadUserId == currentHeadUserId)
            throw new DomainException("That member is already the family head.");
        if (newHeadUserId == Guid.Empty)
            throw new DomainException("A new head is required.");

        HeadUserId = newHeadUserId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Gives a headless Family a head — the next joiner (menunest-201 rule 4).</summary>
    public void AssignHead(Guid userId)
    {
        if (HeadUserId is not null)
            throw new DomainException("This family already has a head.");
        if (userId == Guid.Empty)
            throw new DomainException("A head is required.");

        HeadUserId = userId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Leaves the Family headless — only when its last member leaves.</summary>
    public void ClearHead()
    {
        HeadUserId = null;
        UpdatedAt = DateTime.UtcNow;
    }
```

- [ ] **Step 4: Map the column**

In `FamilyConfiguration.cs`, beside the existing `CreatedByUserId` property line:

```csharp
        // Nullable: a Family whose last member left has no head until the next
        // person joins (menunest-201). No FK to Users — the head is always a
        // member, but membership lives on User.FamilyId, so a FK here would be
        // a second, contradictable source of truth.
        b.Property(x => x.HeadUserId);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS. Existing `Family` tests should be unaffected; if one asserts on a full object graph it may need `HeadUserId` added.

- [ ] **Step 6: Create the migration**

```bash
cd backend
DOTNET_ROOT=/opt/homebrew/Cellar/dotnet/10.0.400/libexec ~/.dotnet/tools/dotnet-ef \
  migrations add AddFamilyHead \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

- [ ] **Step 7: Add the backfill to the migration by hand**

The generated `Up()` only adds the column. Append the backfill from menunest-201 rule 5 — the creator if they are still a member, otherwise the earliest-joined current member:

```csharp
            migrationBuilder.Sql(@"
UPDATE f
SET f.HeadUserId = COALESCE(
    (SELECT u.Id FROM Users u WHERE u.Id = f.CreatedByUserId AND u.FamilyId = f.Id),
    (SELECT TOP 1 u2.Id FROM Users u2 WHERE u2.FamilyId = f.Id ORDER BY u2.JoinedAt)
)
FROM Families f;");
```

A Family with no members at all keeps `NULL`, which is the correct headless state.

Leave `Down()` as generated — dropping the column discards the backfill, which is what reverting means.

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/Family.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Configurations/FamilyConfiguration.cs \
        backend/src/MenuNest.Infrastructure/Persistence/Migrations/ \
        backend/tests/MenuNest.Application.UnitTests/Families/FamilyHeadTests.cs
git commit -m "feat(family): give every family a transferable head (#106)"
```

---

### Task 2: The head cannot leave while other members remain

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Families/LeaveFamily/LeaveFamilyHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Families/LeaveFamilyHeadGuardTests.cs`

**Interfaces:**
- Consumes: `Family.HeadUserId`, `Family.ClearHead()`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Families.LeaveFamily;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Families;

public class LeaveFamilyHeadGuardTests
{
    private static User AddSecondMember(HandlerTestFixture fx)
    {
        var other = User.CreateFromExternalLogin(
            externalId: "other-oid", email: "other@example.com",
            displayName: "Other Member", authProvider: AuthProvider.Microsoft);
        other.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(other);
        fx.Db.SaveChanges();
        return other;
    }

    [Fact]
    public async Task The_head_cannot_leave_while_another_member_remains()
    {
        using var fx = new HandlerTestFixture();   // fx.User created the family, so is head
        AddSecondMember(fx);

        var sut = new LeaveFamilyHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(new LeaveFamilyCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*hand over*");
    }

    [Fact]
    public async Task The_head_may_leave_as_the_last_member_and_the_family_becomes_headless()
    {
        using var fx = new HandlerTestFixture();

        await new LeaveFamilyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new LeaveFamilyCommand(), CancellationToken.None);

        fx.Db.Families.Single().HeadUserId.Should().BeNull();
        fx.Db.Users.Single(u => u.Id == fx.User.Id).FamilyId.Should().BeNull();
    }

    [Fact]
    public async Task A_member_who_is_not_the_head_may_leave_freely()
    {
        using var fx = new HandlerTestFixture();
        var other = AddSecondMember(fx);
        fx.UserProvisioner
            .Setup(u => u.RequireFamilyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((other, fx.Family.Id));

        await new LeaveFamilyHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new LeaveFamilyCommand(), CancellationToken.None);

        fx.Db.Families.Single().HeadUserId.Should().Be(fx.User.Id);
        fx.Db.Users.Single(u => u.Id == other.Id).FamilyId.Should().BeNull();
    }
}
```

Add `using Moq;` at the top for `It.IsAny`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~LeaveFamilyHeadGuardTests`
Expected: FAIL — the head leaves without complaint and `HeadUserId` still points at them.

- [ ] **Step 3: Add the guard**

In `LeaveFamilyHandler.Handle`, after `RequireFamilyAsync` and before removing the relationships:

```csharp
        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == familyId, ct)
            ?? throw new DomainException("Family not found.");

        if (family.HeadUserId == user.Id)
        {
            // menunest-201: authority is always taken deliberately. Auto-passing
            // the role would hand somebody power they never asked for and may
            // not notice. The escape is never blocked — hand over, then leave.
            var othersRemain = await _db.Users
                .AnyAsync(u => u.FamilyId == familyId && u.Id != user.Id, ct);
            if (othersRemain)
                throw new DomainException(
                    "You are the family head. Hand the role over to another member before you leave.");

            family.ClearHead();
        }
```

Change the destructure at the top from `var (user, familyId)` if it is not already that shape.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Families/LeaveFamily/LeaveFamilyHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Families/LeaveFamilyHeadGuardTests.cs
git commit -m "feat(family): the head must hand over before leaving (#106)"
```

---

### Task 3: A headless family adopts its next joiner

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Families/JoinFamily/JoinFamilyHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Families/JoinFamilyAdoptsHeadTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Families.JoinFamily;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Moq;

namespace MenuNest.Application.UnitTests.Families;

public class JoinFamilyAdoptsHeadTests
{
    private static (HandlerTestFixture fx, User joiner) Arrange(bool headless)
    {
        var fx = new HandlerTestFixture();
        if (headless) fx.Db.Families.Single().ClearHead();

        var joiner = User.CreateFromExternalLogin(
            externalId: "joiner-oid", email: "joiner@example.com",
            displayName: "Joiner", authProvider: AuthProvider.Microsoft);
        fx.Db.Users.Add(joiner);
        fx.Db.SaveChanges();

        fx.UserProvisioner
            .Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(joiner);
        return (fx, joiner);
    }

    [Fact]
    public async Task A_headless_family_makes_its_next_joiner_the_head()
    {
        var (fx, joiner) = Arrange(headless: true);
        using var _ = fx;
        var code = fx.Db.Families.Single().InviteCode.Value;

        await new JoinFamilyHandler(fx.Db, fx.UserProvisioner.Object, new JoinFamilyValidator())
            .Handle(new JoinFamilyCommand(code), CancellationToken.None);

        fx.Db.Families.Single().HeadUserId.Should().Be(joiner.Id);
    }

    [Fact]
    public async Task A_family_that_has_a_head_keeps_it_when_someone_joins()
    {
        var (fx, joiner) = Arrange(headless: false);
        using var _ = fx;
        var code = fx.Db.Families.Single().InviteCode.Value;

        await new JoinFamilyHandler(fx.Db, fx.UserProvisioner.Object, new JoinFamilyValidator())
            .Handle(new JoinFamilyCommand(code), CancellationToken.None);

        fx.Db.Families.Single().HeadUserId.Should().Be(fx.User.Id);
        fx.Db.Families.Single().HeadUserId.Should().NotBe(joiner.Id);
    }
}
```

> Check `JoinFamilyCommand`'s real shape before running — if it takes more than the invite code, pass what it needs.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~JoinFamilyAdoptsHeadTests`
Expected: FAIL — the headless family stays headless.

- [ ] **Step 3: Adopt the joiner**

In `JoinFamilyHandler.Handle`, immediately after `user.JoinFamily(family.Id);`:

```csharp
        // menunest-201 rule 4: a Family whose last member left has no head, and
        // the next person to join takes the role. Without this a headless Family
        // could never regain one, so no member could ever undo another's change.
        if (family.HeadUserId is null) family.AssignHead(user.Id);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Families/JoinFamily/JoinFamilyHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Families/JoinFamilyAdoptsHeadTests.cs
git commit -m "feat(family): a headless family adopts its next joiner as head (#106)"
```

---

### Task 4: The transfer endpoint

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Families/TransferHead/TransferHeadCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/TransferHead/TransferHeadHandler.cs`
- Modify: the families controller (`grep -rl "families" backend/src/MenuNest.WebApi/Controllers`)
- Test: `backend/tests/MenuNest.Application.UnitTests/Families/TransferHeadHandlerTests.cs`

**Interfaces:**
- Produces: `POST /api/families/head` with body `{ "newHeadUserId": "<guid>" }`, returning 204.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Families.TransferHead;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Moq;

namespace MenuNest.Application.UnitTests.Families;

public class TransferHeadHandlerTests
{
    private static User AddSecondMember(HandlerTestFixture fx)
    {
        var other = User.CreateFromExternalLogin(
            externalId: "other-oid", email: "other@example.com",
            displayName: "Other Member", authProvider: AuthProvider.Microsoft);
        other.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(other);
        fx.Db.SaveChanges();
        return other;
    }

    [Fact]
    public async Task The_head_hands_the_role_to_another_member()
    {
        using var fx = new HandlerTestFixture();
        var other = AddSecondMember(fx);

        await new TransferHeadHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new TransferHeadCommand(other.Id), CancellationToken.None);

        fx.Db.Families.Single().HeadUserId.Should().Be(other.Id);
    }

    [Fact]
    public async Task A_member_who_is_not_the_head_cannot_transfer_it()
    {
        using var fx = new HandlerTestFixture();
        var other = AddSecondMember(fx);
        fx.UserProvisioner
            .Setup(u => u.RequireFamilyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((other, fx.Family.Id));

        var act = async () => await new TransferHeadHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new TransferHeadCommand(fx.User.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*only the family head*");
    }

    [Fact]
    public async Task The_role_cannot_be_handed_to_someone_outside_the_family()
    {
        using var fx = new HandlerTestFixture();
        var stranger = User.CreateFromExternalLogin(
            "stranger-oid", "stranger@example.com", "Stranger", AuthProvider.Microsoft);
        fx.Db.Users.Add(stranger);
        await fx.Db.SaveChangesAsync();

        var act = async () => await new TransferHeadHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new TransferHeadCommand(stranger.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*not a member*");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~TransferHeadHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Write the command and handler**

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Families.TransferHead;

public sealed record TransferHeadCommand(Guid NewHeadUserId) : ICommand<Unit>;
```

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.TransferHead;

public sealed class TransferHeadHandler : ICommandHandler<TransferHeadCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public TransferHeadHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(TransferHeadCommand cmd, CancellationToken ct)
    {
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == familyId, ct)
            ?? throw new DomainException("Family not found.");

        var isMember = await _db.Users
            .AnyAsync(u => u.Id == cmd.NewHeadUserId && u.FamilyId == familyId, ct);
        if (!isMember)
            throw new DomainException("That person is not a member of this family.");

        // The entity enforces "only the current head" so the rule lives in one
        // place and cannot be bypassed by a second caller later.
        family.TransferHeadTo(user.Id, cmd.NewHeadUserId);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

- [ ] **Step 4: Add the endpoint**

In the families controller, matching its existing style:

```csharp
    [HttpPost("head")]
    public async Task<IActionResult> TransferHead([FromBody] TransferHeadCommand cmd, CancellationToken ct)
    { await _m.Send(cmd, ct); return NoContent(); }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Families/TransferHead/ \
        backend/src/MenuNest.WebApi/Controllers/ \
        backend/tests/MenuNest.Application.UnitTests/Families/TransferHeadHandlerTests.cs
git commit -m "feat(family): the head can hand the role to another member (#106)"
```

---

### Task 5: The head may undo and redo any member's change

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/History/UndoChange/UndoChangeHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/History/RedoChange/RedoChangeHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/History/HeadUndoesAnyoneTests.cs`

**Interfaces:**
- Consumes: `Family.HeadUserId`.

This is the seam the previous plan marked in both handlers. Widen it there and nowhere else.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Application.UseCases.Budget.History.UndoChange;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Moq;

namespace MenuNest.Application.UnitTests.Budget.History;

public class HeadUndoesAnyoneTests
{
    private static (User other, BudgetChange change) Seed(HandlerTestFixture fx)
    {
        var other = User.CreateFromExternalLogin(
            "other-oid", "other@example.com", "Other Member", AuthProvider.Microsoft);
        other.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(other);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 300m));

        var change = BudgetChange.RecordAssign(fx.Family.Id, other.Id, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(change);
        fx.Db.SaveChanges();
        return (other, change);
    }

    [Fact]
    public async Task The_head_may_undo_another_members_change()
    {
        using var fx = new HandlerTestFixture();   // fx.User created the family, so is head
        var (_, change) = Seed(fx);

        await new UndoChangeHandler(fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock)
            .Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        fx.Db.MonthlyAssignments.Single().AssignedAmount.Should().Be(0m);
        fx.Db.BudgetChanges.Single().UndoneByUserId.Should().Be(fx.User.Id);
    }

    [Fact]
    public async Task An_ordinary_member_still_cannot_undo_another_members_change()
    {
        using var fx = new HandlerTestFixture();
        var (other, _) = Seed(fx);

        // A third member: not the head, not the author.
        var third = User.CreateFromExternalLogin(
            "third-oid", "third@example.com", "Third", AuthProvider.Microsoft);
        third.JoinFamily(fx.Family.Id);
        fx.Db.Users.Add(third);
        await fx.Db.SaveChangesAsync();
        fx.UserProvisioner
            .Setup(u => u.RequireFamilyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((third, fx.Family.Id));

        var change = fx.Db.BudgetChanges.Single();
        var act = async () =>
            await new UndoChangeHandler(fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock)
                .Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*your own*");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~HeadUndoesAnyoneTests`
Expected: the first test FAILS with "You can only undo your own changes"; the second passes already.

- [ ] **Step 3: Widen the seam in `UndoChangeHandler`**

Replace the ownership check:

```csharp
        // menunest-198: a member may undo their own; the FAMILY HEAD may undo
        // anyone's. This is the app's only permission distinction, and
        // menunest-201 keeps it to exactly this one power.
        if (change.UserId != user.Id)
        {
            var isHead = await _db.Families
                .AnyAsync(f => f.Id == familyId && f.HeadUserId == user.Id, ct);
            if (!isHead)
                throw new DomainException("You can only undo your own changes.");
        }
```

- [ ] **Step 4: Widen the same seam in `RedoChangeHandler`**

Identical, with `"You can only redo your own changes."`

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/History/ \
        backend/tests/MenuNest.Application.UnitTests/Budget/History/HeadUndoesAnyoneTests.cs
git commit -m "feat(budget): the family head may undo any member's change (#106)"
```

---

### Task 6: A generic push method on `IWebPushSender`

**Files:**
- Modify: `backend/src/MenuNest.Application/Abstractions/IWebPushSender.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Services/WebPushSender.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Services/NullWebPushSender.cs`
- Test: `backend/tests/MenuNest.Infrastructure.IntegrationTests/` — follow whatever pattern that project already uses for `WebPushSender`; if it has none, skip the test here and rely on Task 7's handler test with a mocked sender.

**Interfaces:**
- Produces: `Task<int> SendToUserAsync(Guid userId, string title, string body, CancellationToken ct = default)` — returns how many devices were reached.

- [ ] **Step 1: Add the method to the interface**

```csharp
    /// <summary>
    /// Pushes a plain title/body to every active subscription belonging to
    /// <paramref name="userId"/>. Returns the count reached — 0 when the user
    /// has granted no permission, which is a normal outcome, not an error.
    ///
    /// <para>Added for menunest-201: when the family head undoes a member's
    /// change, that member is told. Best-effort by design — requiring push
    /// would block a legitimate correction on a permission the member may
    /// never have granted.</para>
    /// </summary>
    Task<int> SendToUserAsync(Guid userId, string title, string body, CancellationToken ct = default);
```

- [ ] **Step 2: Implement it in `WebPushSender`**

Read the existing `SendFollowUpAsync` first: it resolves subscriptions by `UserId`, sends a VAPID-signed payload per subscription, deletes rows on 404/410 and calls `RecordFailure` otherwise. The new method is that same loop without the episode lookup. **Extract the per-subscription send loop into a private helper and have both methods call it** — do not copy the failure handling, which is the part that must not drift.

- [ ] **Step 3: Implement it in `NullWebPushSender`**

```csharp
    public Task<int> SendToUserAsync(Guid userId, string title, string body, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "NullWebPushSender: would push to user {UserId}: {Title}", userId, title);
        return Task.FromResult(0);
    }
```

- [ ] **Step 4: Run the tests and commit**

Run: `cd backend && dotnet test`
Expected: PASS.

```bash
git add backend/src/MenuNest.Application/Abstractions/IWebPushSender.cs \
        backend/src/MenuNest.Infrastructure/Services/
git commit -m "feat(push): a generic per-user send on IWebPushSender (#106)"
```

---

### Task 7: Tell the member when the head undoes their change

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Budget/History/UndoChange/UndoChangeHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Budget/History/HeadUndoNotifiesTests.cs`

- [ ] **Step 1: Write the failing test**

Build on `HeadUndoesAnyoneTests`'s `Seed`. Pass a `Mock<IWebPushSender>` into the handler and assert:

```csharp
    [Fact]
    public async Task The_author_is_notified_when_the_head_undoes_their_change()
    {
        using var fx = new HandlerTestFixture();
        var (other, change) = Seed(fx);
        var push = new Mock<IWebPushSender>();

        await new UndoChangeHandler(
                fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock, push.Object)
            .Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        push.Verify(p => p.SendToUserAsync(
            other.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Undoing_my_own_change_notifies_nobody()
    {
        using var fx = new HandlerTestFixture();
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 8, 300m));
        var mine = BudgetChange.RecordAssign(fx.Family.Id, fx.User.Id, 2026, 8, cat.Id, 300m, null);
        fx.Db.BudgetChanges.Add(mine);
        await fx.Db.SaveChangesAsync();
        var push = new Mock<IWebPushSender>();

        await new UndoChangeHandler(
                fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock, push.Object)
            .Handle(new UndoChangeCommand(mine.Id), CancellationToken.None);

        push.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failing_push_does_not_fail_the_undo()
    {
        using var fx = new HandlerTestFixture();
        var (_, change) = Seed(fx);
        var push = new Mock<IWebPushSender>();
        push.Setup(p => p.SendToUserAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("push is down"));

        await new UndoChangeHandler(
                fx.Db, fx.UserProvisioner.Object, new BudgetChangeApplier(fx.Db), fx.Clock, push.Object)
            .Handle(new UndoChangeCommand(change.Id), CancellationToken.None);

        fx.Db.BudgetChanges.Single().IsUndone.Should().BeTrue();
    }
```

The third test is the important one: best-effort must mean best-effort.

- [ ] **Step 2: Run the tests to verify they fail**

Expected: FAIL to compile — the handler takes four constructor arguments.

- [ ] **Step 3: Notify, after the save, and swallow failures**

Add `IWebPushSender _push` as a fifth constructor parameter, then after `await _db.SaveChangesAsync(ct);`:

```csharp
        // menunest-201: the author is told when somebody else undid their work.
        // AFTER the save, so a push failure can never roll back a completed
        // undo, and wrapped, because best-effort must actually be best-effort —
        // the Change history row names the undoer regardless, which is the
        // notice that always lands.
        if (change.UserId != user.Id)
        {
            try
            {
                await _push.SendToUserAsync(
                    change.UserId,
                    "A budget change was undone",
                    $"{user.DisplayName} undid one of your budget changes.",
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Undo notification failed for change {ChangeId}; the undo itself succeeded.",
                    change.Id);
            }
        }
```

Add `ILogger<UndoChangeHandler> _logger` as a sixth constructor parameter, and pass `NullLogger<UndoChangeHandler>.Instance` in the tests (`using Microsoft.Extensions.Logging.Abstractions;`).

- [ ] **Step 4: Fix every existing `new UndoChangeHandler(...)` call site**

Run: `cd backend && dotnet build` and add the two new arguments to each.

- [ ] **Step 5: Run the tests and commit**

Run: `cd backend && dotnet test`
Expected: PASS.

```bash
git add backend/src/MenuNest.Application/UseCases/Budget/History/UndoChange/UndoChangeHandler.cs \
        backend/tests/MenuNest.Application.UnitTests/Budget/History/
git commit -m "feat(budget): tell a member when the head undoes their change (#106)"
```

---

### Task 8: Show who the head is

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Families/FamilyMemberDto.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Families/ListFamilyMembers/ListFamilyMembersHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Families/ListFamilyMembersHeadTests.cs`

`FamilyMemberDto` already carries `IsCreator`, so `IsHead` follows an established pattern rather than inventing one. **menunest-201 records that where the badge appears was derived, not asked** — this task exposes the field; placing it in the SPA belongs to the frontend plan.

- [ ] **Step 1: Write the failing test**

Arrange a fixture with a second member, call `ListFamilyMembersHandler`, and assert `IsHead` is true for `fx.User` and false for the other. Read the handler first for its exact constructor and query shape.

- [ ] **Step 2: Run it to verify it fails**

Expected: FAIL to compile — `IsHead` does not exist.

- [ ] **Step 3: Add the field**

```csharp
public sealed record FamilyMemberDto(
    Guid UserId,
    string DisplayName,
    string Email,
    DateTime JoinedAt,
    bool IsCreator,
    bool IsHead,
    RelationshipLabelDto[] Relationships);
```

Then set it in `ListFamilyMembersHandler` from the family's `HeadUserId`, and fix any other construction the compiler names.

- [ ] **Step 4: Run the tests and commit**

Run: `cd backend && dotnet test`
Expected: PASS.

```bash
git add backend/src/MenuNest.Application/UseCases/Families/ \
        backend/tests/MenuNest.Application.UnitTests/Families/
git commit -m "feat(family): expose which member is the head (#106)"
```

---

### Task 9: Apply the migration to prod, by hand

**Files:** none.

Identical in shape to the previous plan's Task 8, and with the same ordering rule: **after** the code is deployed, never before. This migration also **writes data** (the backfill), so read the SQL before running it.

- [ ] **Step 1: Preview the SQL**

```bash
cd backend
DOTNET_ROOT=/opt/homebrew/Cellar/dotnet/10.0.400/libexec ~/.dotnet/tools/dotnet-ef \
  migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --output /tmp/familyhead.sql
```

Confirm it adds `HeadUserId` to `Families` and runs the backfill `UPDATE`, and nothing else.

- [ ] **Step 2: Open the SQL firewall for your IP, temporarily**

```bash
IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply \
  --start-ip-address $IP --end-ip-address $IP
```

- [ ] **Step 3: Apply it**

```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential DOTNET_ROOT=/opt/homebrew/Cellar/dotnet/10.0.400/libexec \
  ~/.dotnet/tools/dotnet-ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```

`AZURE_TOKEN_CREDENTIALS=AzureCliCredential` is required, or SqlClient picks the Visual Studio **work** account and the login fails. Confirm `az account show` reports `Pay-As-You-Go` / `personal@example.com` first.

- [ ] **Step 4: Close the firewall again**

```bash
az sql server firewall-rule delete --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply
```

- [ ] **Step 5: Verify the backfill actually landed**

Open `/family` in prod. Exactly one member must come back with `IsHead: true`. If every member shows false, the backfill matched nothing — check that the Family's `CreatedByUserId` names a current member and that `Users.JoinedAt` is populated.

---

## Self-Review

**Spec coverage.** menunest-198 (the head may undo anyone; rows name the actor) — Task 5, plus Task 8 for the badge data. menunest-201 rule 1 (creator is first head) — Task 1 Step 3. Rule 2 (only the head transfers) — Task 1's entity guard plus Task 4. Rule 3 (cannot leave while others remain) — Task 2. Rule 4 (last member leaves, next joiner takes it) — Tasks 2 and 3 together. Rule 5 (backfill) — Task 1 Step 7. Exactly-one-power — enforced by omission and called out in Global Constraints. Notification — Tasks 6 and 7.

**Not covered here, by design:** every frontend file. The transfer UI, the head badge and the undo button's widened availability are the frontend plan.

**The riskiest task is 7**, not 5. Wrapping the push in a try/catch is easy to write and easy to get subtly wrong — a swallowed exception that also swallows a genuine bug. The third test exists precisely to pin the intended behaviour: the undo completes, the push fails, and the failure is logged rather than silent.

**One thing this plan deliberately does not do:** refactor `SendFollowUpAsync` to call the new generic method. Task 6 extracts the shared send loop, which is enough; rewriting the health path's public shape is unrelated risk on a plan about permissions.
