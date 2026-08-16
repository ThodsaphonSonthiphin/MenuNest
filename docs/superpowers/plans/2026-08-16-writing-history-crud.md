# Writing Practice — History Screen CRUD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use sp-subagent-driven-development (recommended) or sp-executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the "ประวัติ" (History) screen for MenuNest's writing-practice feature — a full-list
view of every past `WritingEntry` with a per-row pending/corrected status badge and a tappable
filter, plus open/edit/soft-delete for each entry — so the writer can see and manage past nights
now that correction runs on its own schedule via Claude Code.

**Architecture:** Standard MenuNest CQRS slice, extending the existing `WritingEntry` vertical
(Phase 1, issue #97) rather than starting a new one: a `SoftDelete()` + `UpdateText()` pair of
domain methods (mirroring `Drug`'s soft-delete convention exactly), three new
`Mediator` use cases (`ListWritingEntries`, `UpdateWritingEntryText`, `DeleteWritingEntry`) added
to the existing `WritingEntriesController`, and two new React pages
(`WritingHistoryPage`, `WritingEntryDetailPage`) wired under `/writing/history` — no new nav item,
reached instead by a "ดูประวัติทั้งหมด" link from the existing เขียน page, mirroring the Health
module's `/health` → `/health/history` pattern exactly.

**Tech Stack:** .NET 10 / EF Core / `Mediator` (source-gen CQRS) / FluentValidation / xUnit + Moq +
FluentAssertions — React 19 / TypeScript / RTK Query / `@syncfusion/react-grid` (already a project
dependency, its `material.css` already imported in `main.tsx`, but **used here for the first time**
in this app) / `@syncfusion/ej2-react-richtexteditor` (already used by the เขียน page, reused here
for the edit view) / vitest (pure-logic only — this plan adds none, see Global Constraints).

**Spec:** `docs/decision-map/writing-practice-build/tickets/entry-mutability.md` +
[ADR-169](../adr/169-a-corrected-entry-locks-a-deleted-entry-soft-deletes.md) (full CRUD via a new
History screen; correction locks text; delete is soft) and
`docs/decision-map/writing-practice-build/tickets/pending-correction-visibility.md` (per-row status
badge + tappable filter inside that screen, built as a Syncfusion `react-grid` DataGrid, no nav
badge). No Claude Design mock exists for this screen — the ASCII-mock exchange recorded on
`pending-correction-visibility`'s resolution (per-row badge column + top filter chips, confirmed by
the writer) is the closest thing to an approved shape; Task 11's interactive verification checks
the build against that shape.

## Global Constraints

- Every commit references issue **#97** (`(#97)` in the subject — this plan does not close it,
  since the progress screen and MCP correction tools are still separate work).
- `git add` **explicit paths only** — never `-A` / `.` (project rule).
- The pre-commit hook runs the **full** backend + frontend suite; every commit must leave it green.
- `WritingEntries` is already a `DbSet<>` on all three `IApplicationDbContext` implementers
  (`AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext`) from Phase 1 — **no DbSet change
  needed in this plan**, only the entity itself gains a column (Task 1/2).
- **Every query against `WritingEntry` must filter `DeletedAt == null`** (ADR-169) — List, Update,
  and Delete's own "find the entry" lookup all apply this filter.
- **A correction locks the text, not the row.** `UpdateWritingEntryText` must reject an edit once
  `CorrectedAt` is set (`DomainException`, enforced in the domain method, not just the UI); `Delete`
  has no such guard — a locked entry can still be deleted (ADR-169).
- **No correction UI, no progress screen, no MCP tools in this plan.** Those remain separate work
  (`ai-correction-invocation`/`mcp-tool-contract` are already resolved elsewhere; nothing here gives
  `WritingEntry.CorrectedAt` a public setter — that arrives with Phase 2's `record_writing_correction`
  MCP tool).
- **This repo's frontend has no component/visual test harness** (`vitest` runs in Node, no jsdom) —
  every frontend task in this plan is UI wiring with no pure-logic extraction opportunity (unlike
  Phase 1's timer math), so **Task 11's interactive verification is mandatory**, not optional.
- The prod SQL migration (the new `DeletedAt` column) is applied **by hand** after merge — see
  Task 11's first step, and `CLAUDE.md`'s "Database migrations are applied MANUALLY" section for the
  exact command.

---

### Task 1: Domain — `WritingEntry.SoftDelete()` + `UpdateText()`

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/WritingEntry.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Writing/WritingEntryTests.cs`

**Interfaces:**
- Consumes: nothing new — extends the existing `WritingEntry` entity from Phase 1.
- Produces: `WritingEntry.DeletedAt` (`DateTime?`, mirrors `Drug.DeletedAt`),
  `WritingEntry.SoftDelete()` (no args), `WritingEntry.UpdateText(string text)` (throws
  `DomainException` if `CorrectedAt` is set, or if the text has no words). Consumed by Task 3's
  List projection, Task 4's update handler, and Task 5's delete handler.

- [ ] **Step 1: Write the failing tests**

Append to the existing `WritingEntryTests.cs` (keep every existing test in the file — this only
adds new `[Fact]`s):

```csharp
    [Fact]
    public void SoftDelete_sets_DeletedAt()
    {
        var entry = WritingEntry.Create(Guid.NewGuid(), Today, "<p>a night to delete</p>", 420);
        entry.DeletedAt.Should().BeNull();

        entry.SoftDelete();

        entry.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateText_changes_text_when_not_yet_corrected()
    {
        var entry = WritingEntry.Create(Guid.NewGuid(), Today, "<p>original text here</p>", 420);

        entry.UpdateText("<p>edited text here</p>");

        entry.Text.Should().Be("<p>edited text here</p>");
    }

    [Fact]
    public void UpdateText_throws_when_text_is_empty_or_whitespace_only_html()
    {
        var entry = WritingEntry.Create(Guid.NewGuid(), Today, "<p>original text here</p>", 420);

        var act = () => entry.UpdateText("<p>   </p>");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateText_throws_when_the_entry_is_already_corrected()
    {
        // CorrectedAt has no public setter yet -- it stays reserved until Phase
        // 2's record_writing_correction MCP tool lands (mcp-tool-contract).
        // Set it via reflection here purely to exercise the lock guard; this is
        // not how CorrectedAt gets set in production.
        var entry = WritingEntry.Create(Guid.NewGuid(), Today, "<p>original text here</p>", 420);
        typeof(WritingEntry).GetProperty(nameof(WritingEntry.CorrectedAt))!
            .SetValue(entry, DateTime.UtcNow);

        var act = () => entry.UpdateText("<p>trying to edit this now</p>");

        act.Should().Throw<DomainException>();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter WritingEntryTests`
Expected: FAIL — `DeletedAt`, `SoftDelete`, `UpdateText` do not exist yet (CS1061/CS0117).

- [ ] **Step 3: Add the property and methods**

In `WritingEntry.cs`, add the property next to the other fields (after `StuckWordsJson`):

```csharp
    public DateTime? DeletedAt { get; private set; }
```

Add the two methods after the existing `Create` factory (before the private `CountWords` helper):

```csharp
    /// <summary>
    /// Edits the text of an entry that has not yet been corrected. Once
    /// CorrectedAt is set, the recorded HitCount/MissCount/ThaiWhyLine
    /// describe specific text -- letting that text drift under them would
    /// make the correction lie, so entry-mutability (ADR-169) locks it.
    /// WordsPerMinute is deliberately left unchanged: it measures the
    /// original timed writing session, not a later typo fix.
    /// </summary>
    public void UpdateText(string text)
    {
        if (CorrectedAt is not null)
            throw new DomainException("Cannot edit text after a correction has been recorded.");

        var wordCount = CountWords(text);
        if (wordCount == 0)
            throw new DomainException("Text must contain at least one word.");

        Text = text;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Soft-deletes the entry (ADR-169) -- allowed even when the text is
    /// locked by a correction; the lock only blocks edits, not deletion.
    /// The row stays so the monthly old-vs-new comparison (progress-signal)
    /// can still resolve a deleted night if the rotation lands on it.
    /// </summary>
    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter WritingEntryTests`
Expected: PASS (10 tests total — 6 existing + 4 new).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/WritingEntry.cs backend/tests/MenuNest.Application.UnitTests/Writing/WritingEntryTests.cs
git commit -m "feat(writing): add WritingEntry.SoftDelete and UpdateText (#97)"
```

---

### Task 2: Persistence — `DeletedAt` column + migration

**Files:**
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/WritingEntryConfiguration.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Persistence/WritingEntryConfigurationTests.cs`
- Create (generated): `backend/src/MenuNest.Infrastructure/Persistence/Migrations/*_AddWritingEntryDeletedAt.cs`
  (+ `.Designer.cs`, regenerated `AppDbContextModelSnapshot.cs`)

**Interfaces:**
- Consumes: `WritingEntry.DeletedAt` (Task 1).
- Produces: a `DeletedAt` column on the `WritingEntries` table, and an `(UserId, DeletedAt)` index
  (mirroring `DrugConfiguration`) for Task 3's List query.

- [ ] **Step 1: Map the column and add the index**

In `WritingEntryConfiguration.cs`, add next to the `ThaiWhyLine` mapping:

```csharp
        builder.Property(w => w.DeletedAt);
```

Add a second index next to the existing `(UserId, Date)` one:

```csharp
        // Hot query for the History screen's ListWritingEntries (UserId + DeletedAt IS NULL),
        // mirroring DrugConfiguration's (UserId, DeletedAt) index.
        builder.HasIndex(w => new { w.UserId, w.DeletedAt });
```

- [ ] **Step 2: Extend the existing round-trip test**

In `WritingEntryConfigurationTests.cs`, add a second `[Fact]` after the existing
`Round_trips_a_writing_entry_through_sqlite`:

```csharp
    [Fact]
    public async Task Soft_deleted_entry_keeps_its_row_and_records_DeletedAt()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewContext(conn);

        var user = User.CreateFromExternalLogin(
            externalId: "wp-test-oid-2",
            email: "wp2@example.com",
            displayName: "WP Test 2",
            authProvider: AuthProvider.Microsoft);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = WritingEntry.Create(
            user.Id, new DateOnly(2026, 8, 16), "<p>a night to soft delete</p>", 420);
        db.WritingEntries.Add(entry);
        await db.SaveChangesAsync();

        entry.SoftDelete();
        await db.SaveChangesAsync();

        var reloaded = await db.WritingEntries.SingleAsync(w => w.Id == entry.Id);
        reloaded.DeletedAt.Should().NotBeNull();
    }
```

- [ ] **Step 3: Run the test to verify it fails (column not yet migrated)**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter WritingEntryConfigurationTests`
Expected: FAIL to compile — `Property` mapping alone is enough for SQLite's `EnsureCreated()` to
pick up the new column (no migration needed for the test's in-memory SQLite), so this may actually
PASS once Step 1 is in place even before Step 4's migration exists. If it passes here, that is
correct — `EnsureCreated()` builds the schema straight from the model, unlike a real deploy which
needs the migration from Step 4.

- [ ] **Step 4: Generate the migration**

```bash
cd backend
dotnet ef migrations add AddWritingEntryDeletedAt --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Inspect the generated migration's `Up()` — confirm it adds a nullable `DeletedAt` column to
`WritingEntries` and creates the `(UserId, DeletedAt)` index, and nothing else changes.

- [ ] **Step 5: Run the full backend test suite**

Run: `dotnet test backend/tests`
Expected: PASS, including both `WritingEntryConfigurationTests` facts.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/Persistence/Configurations/WritingEntryConfiguration.cs backend/src/MenuNest.Infrastructure/Persistence/Migrations backend/tests/MenuNest.Application.UnitTests/Persistence/WritingEntryConfigurationTests.cs
git commit -m "feat(writing): add WritingEntry.DeletedAt column + migration (#97)"
```

---

### Task 3: Application — `WritingEntryDto.CorrectedAt` + `ListWritingEntries`

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Writing/SubmitWritingEntry/SubmitWritingEntryHandler.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Writing/SubmitWritingEntryHandlerTests.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Writing/ListWritingEntries/ListWritingEntriesQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Writing/ListWritingEntries/ListWritingEntriesHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Writing/ListWritingEntriesHandlerTests.cs`

**Interfaces:**
- Consumes: `WritingEntry` (+ `DeletedAt` from Task 1), `IApplicationDbContext.WritingEntries`,
  `IUserProvisioner.GetOrProvisionCurrentAsync(ct)`.
- Produces: `WritingEntryDto` now carries `DateTime? CorrectedAt`; `ListWritingEntriesQuery :
  IQuery<IReadOnlyList<WritingEntryDto>>` — the exact shape `WritingEntriesController` (Task 6) and
  `WritingHistoryPage` (Task 8) consume.

- [ ] **Step 1: Add `CorrectedAt` to the shared DTO**

In `WritingDtos.cs`, replace the record with:

```csharp
namespace MenuNest.Application.UseCases.Writing;

/// <summary>
/// A writing-practice entry, as returned by submit, list, and update-text.
/// </summary>
public sealed record WritingEntryDto(
    Guid Id,
    DateOnly Date,
    string Text,
    int ElapsedSeconds,
    double WordsPerMinute,
    DateTime? CorrectedAt,
    DateTime CreatedAt);
```

- [ ] **Step 2: Update the one existing construction site**

In `SubmitWritingEntryHandler.cs`, add the new named argument to the `return new WritingEntryDto(...)`
call (it uses named args throughout, so position doesn't matter — add it anywhere in the list):

```csharp
        return new WritingEntryDto(
            Id: entry.Id,
            Date: entry.Date,
            Text: entry.Text,
            ElapsedSeconds: entry.ElapsedSeconds,
            WordsPerMinute: entry.WordsPerMinute,
            CorrectedAt: entry.CorrectedAt,
            CreatedAt: entry.CreatedAt);
```

- [ ] **Step 3: Extend the existing Submit handler test**

In `SubmitWritingEntryHandlerTests.cs`, add one assertion line inside
`Creates_entry_scoped_to_current_user_with_computed_words_per_minute`, right after the existing
`result.WordsPerMinute...` line:

```csharp
        result.CorrectedAt.Should().BeNull();
```

- [ ] **Step 4: Run the Submit tests to confirm the DTO change compiles and passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter SubmitWritingEntryHandlerTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Write the failing List query + handler test**

```csharp
// backend/tests/MenuNest.Application.UnitTests/Writing/ListWritingEntriesHandlerTests.cs
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.ListWritingEntries;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Writing;

public class ListWritingEntriesHandlerTests
{
    [Fact]
    public async Task Returns_only_current_users_non_deleted_entries_newest_first()
    {
        using var fx = new HandlerTestFixture();
        var handler = new ListWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var older = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 10), "<p>older entry today</p>", 420);
        var newer = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 15), "<p>newer entry today</p>", 420);
        var deleted = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 14), "<p>deleted entry today</p>", 420);
        deleted.SoftDelete();

        var otherUser = User.CreateFromExternalLogin(
            externalId: "other-oid",
            email: "other@example.com",
            displayName: "Other User",
            authProvider: AuthProvider.Microsoft);
        fx.Db.Users.Add(otherUser);
        var othersEntry = WritingEntry.Create(otherUser.Id, new DateOnly(2026, 8, 16), "<p>not mine today</p>", 420);

        fx.Db.WritingEntries.AddRange(older, newer, deleted, othersEntry);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListWritingEntriesQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(newer.Id);
        result[1].Id.Should().Be(older.Id);
    }
}
```

- [ ] **Step 6: Run the test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter ListWritingEntriesHandlerTests`
Expected: FAIL — `ListWritingEntriesQuery`/`ListWritingEntriesHandler` do not exist yet.

- [ ] **Step 7: Write the query**

```csharp
// backend/src/MenuNest.Application/UseCases/Writing/ListWritingEntries/ListWritingEntriesQuery.cs
using Mediator;

namespace MenuNest.Application.UseCases.Writing.ListWritingEntries;

/// <summary>
/// Lists every non-deleted WritingEntry for the current user, newest first.
/// Feeds the "ประวัติ" (History) screen -- filtering by pending/corrected
/// status happens client-side over this full list
/// (pending-correction-visibility).
/// </summary>
public sealed record ListWritingEntriesQuery : IQuery<IReadOnlyList<WritingEntryDto>>;
```

- [ ] **Step 8: Write the handler**

```csharp
// backend/src/MenuNest.Application/UseCases/Writing/ListWritingEntries/ListWritingEntriesHandler.cs
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.ListWritingEntries;

public sealed class ListWritingEntriesHandler
    : IQueryHandler<ListWritingEntriesQuery, IReadOnlyList<WritingEntryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListWritingEntriesHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<WritingEntryDto>> Handle(
        ListWritingEntriesQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        return await _db.WritingEntries
            .Where(w => w.UserId == user.Id && w.DeletedAt == null)
            .OrderByDescending(w => w.Date)
            .Select(w => new WritingEntryDto(
                w.Id, w.Date, w.Text, w.ElapsedSeconds, w.WordsPerMinute, w.CorrectedAt, w.CreatedAt))
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 9: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "ListWritingEntriesHandlerTests|SubmitWritingEntryHandlerTests|WritingEntryTests"`
Expected: PASS (15 tests total).

- [ ] **Step 10: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs backend/src/MenuNest.Application/UseCases/Writing/SubmitWritingEntry/SubmitWritingEntryHandler.cs backend/tests/MenuNest.Application.UnitTests/Writing/SubmitWritingEntryHandlerTests.cs backend/src/MenuNest.Application/UseCases/Writing/ListWritingEntries backend/tests/MenuNest.Application.UnitTests/Writing/ListWritingEntriesHandlerTests.cs
git commit -m "feat(writing): add CorrectedAt to WritingEntryDto and ListWritingEntries query (#97)"
```

---

### Task 4: Application — `UpdateWritingEntryText`

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Writing/UpdateWritingEntryText/UpdateWritingEntryTextCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Writing/UpdateWritingEntryText/UpdateWritingEntryTextHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Writing/UpdateWritingEntryText/UpdateWritingEntryTextValidator.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Writing/UpdateWritingEntryTextHandlerTests.cs`

**Interfaces:**
- Consumes: `WritingEntry.UpdateText(string)` (Task 1), `IApplicationDbContext.WritingEntries`.
- Produces: `UpdateWritingEntryTextCommand(Guid Id, string Text) : ICommand<WritingEntryDto>` — the
  exact shape `WritingEntriesController` (Task 6) and `WritingEntryDetailPage` (Task 9) use.

- [ ] **Step 1: Write the failing handler tests**

```csharp
// backend/tests/MenuNest.Application.UnitTests/Writing/UpdateWritingEntryTextHandlerTests.cs
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.UpdateWritingEntryText;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Writing;

public class UpdateWritingEntryTextHandlerTests
{
    private static UpdateWritingEntryTextHandler Build(HandlerTestFixture fx)
        => new(fx.Db, fx.UserProvisioner.Object, new UpdateWritingEntryTextValidator());

    [Fact]
    public async Task Updates_text_when_not_yet_corrected()
    {
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>original text today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(
            new UpdateWritingEntryTextCommand(entry.Id, "<p>edited text today</p>"),
            CancellationToken.None);

        result.Text.Should().Be("<p>edited text today</p>");
        var stored = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        stored.Text.Should().Be("<p>edited text today</p>");
    }

    [Fact]
    public async Task Throws_when_entry_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var otherUser = User.CreateFromExternalLogin(
            externalId: "other-oid",
            email: "other@example.com",
            displayName: "Other User",
            authProvider: AuthProvider.Microsoft);
        fx.Db.Users.Add(otherUser);
        var entry = WritingEntry.Create(otherUser.Id, new DateOnly(2026, 8, 16), "<p>not mine today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx).Handle(
            new UpdateWritingEntryTextCommand(entry.Id, "<p>trying to edit today</p>"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Throws_when_entry_is_soft_deleted()
    {
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>gone today</p>", 420);
        entry.SoftDelete();
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx).Handle(
            new UpdateWritingEntryTextCommand(entry.Id, "<p>trying to edit today</p>"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Validator_rejects_empty_text()
    {
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>original text today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Build(fx).Handle(
            new UpdateWritingEntryTextCommand(entry.Id, ""),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
```

Note: the "already corrected -- locked" guard is tested at the domain level in
`WritingEntryTests.UpdateText_throws_when_the_entry_is_already_corrected` (Task 1). This handler
suite covers handler-level concerns only (ownership, soft-delete, validation) — it does not
re-verify the lock, since the handler adds no logic of its own around it (it just calls
`entry.UpdateText(...)`, which is already covered).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter UpdateWritingEntryTextHandlerTests`
Expected: FAIL — the command/handler/validator do not exist yet.

- [ ] **Step 3: Write the command**

```csharp
// backend/src/MenuNest.Application/UseCases/Writing/UpdateWritingEntryText/UpdateWritingEntryTextCommand.cs
using Mediator;

namespace MenuNest.Application.UseCases.Writing.UpdateWritingEntryText;

/// <summary>
/// Edits an existing WritingEntry's text. Only allowed while CorrectedAt is
/// null -- entry-mutability (ADR-169) locks the text the moment a
/// correction is recorded.
/// </summary>
public sealed record UpdateWritingEntryTextCommand(Guid Id, string Text) : ICommand<WritingEntryDto>;
```

- [ ] **Step 4: Write the validator**

```csharp
// backend/src/MenuNest.Application/UseCases/Writing/UpdateWritingEntryText/UpdateWritingEntryTextValidator.cs
using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.UpdateWritingEntryText;

public sealed class UpdateWritingEntryTextValidator : AbstractValidator<UpdateWritingEntryTextCommand>
{
    public UpdateWritingEntryTextValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(50_000);
    }
}
```

- [ ] **Step 5: Write the handler**

```csharp
// backend/src/MenuNest.Application/UseCases/Writing/UpdateWritingEntryText/UpdateWritingEntryTextHandler.cs
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.UpdateWritingEntryText;

public sealed class UpdateWritingEntryTextHandler
    : ICommandHandler<UpdateWritingEntryTextCommand, WritingEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<UpdateWritingEntryTextCommand> _validator;

    public UpdateWritingEntryTextHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<UpdateWritingEntryTextCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<WritingEntryDto> Handle(UpdateWritingEntryTextCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var entry = await _db.WritingEntries
            .FirstOrDefaultAsync(w => w.Id == command.Id && w.UserId == user.Id && w.DeletedAt == null, ct)
            ?? throw new DomainException("Writing entry not found.");

        entry.UpdateText(command.Text);
        await _db.SaveChangesAsync(ct);

        return new WritingEntryDto(
            Id: entry.Id,
            Date: entry.Date,
            Text: entry.Text,
            ElapsedSeconds: entry.ElapsedSeconds,
            WordsPerMinute: entry.WordsPerMinute,
            CorrectedAt: entry.CorrectedAt,
            CreatedAt: entry.CreatedAt);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter UpdateWritingEntryTextHandlerTests`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Writing/UpdateWritingEntryText backend/tests/MenuNest.Application.UnitTests/Writing/UpdateWritingEntryTextHandlerTests.cs
git commit -m "feat(writing): add UpdateWritingEntryText command + handler (#97)"
```

---

### Task 5: Application — `DeleteWritingEntry`

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Writing/DeleteWritingEntry/DeleteWritingEntryCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Writing/DeleteWritingEntry/DeleteWritingEntryHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Writing/DeleteWritingEntryHandlerTests.cs`

**Interfaces:**
- Consumes: `WritingEntry.SoftDelete()` (Task 1), `IApplicationDbContext.WritingEntries`.
- Produces: `DeleteWritingEntryCommand(Guid Id) : ICommand` — consumed by `WritingEntriesController`
  (Task 6) and `WritingEntryDetailPage` (Task 9).

- [ ] **Step 1: Write the failing tests**

```csharp
// backend/tests/MenuNest.Application.UnitTests/Writing/DeleteWritingEntryHandlerTests.cs
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.DeleteWritingEntry;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Writing;

public class DeleteWritingEntryHandlerTests
{
    [Fact]
    public async Task Soft_deletes_the_entry()
    {
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>to be deleted today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);
        await handler.Handle(new DeleteWritingEntryCommand(entry.Id), CancellationToken.None);

        var stored = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        stored.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Allows_deleting_an_already_corrected_locked_entry()
    {
        // entry-mutability (ADR-169): the lock only blocks edits, not deletion.
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>corrected today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();
        typeof(WritingEntry).GetProperty(nameof(WritingEntry.CorrectedAt))!
            .SetValue(entry, DateTime.UtcNow);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);
        var act = async () => await handler.Handle(new DeleteWritingEntryCommand(entry.Id), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Throws_when_entry_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var otherUser = User.CreateFromExternalLogin(
            externalId: "other-oid",
            email: "other@example.com",
            displayName: "Other User",
            authProvider: AuthProvider.Microsoft);
        fx.Db.Users.Add(otherUser);
        var entry = WritingEntry.Create(otherUser.Id, new DateOnly(2026, 8, 16), "<p>not mine today</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);
        var act = async () => await handler.Handle(new DeleteWritingEntryCommand(entry.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Throws_when_entry_already_deleted()
    {
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>already gone today</p>", 420);
        entry.SoftDelete();
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var handler = new DeleteWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);
        var act = async () => await handler.Handle(new DeleteWritingEntryCommand(entry.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter DeleteWritingEntryHandlerTests`
Expected: FAIL — the command/handler do not exist yet.

- [ ] **Step 3: Write the command**

```csharp
// backend/src/MenuNest.Application/UseCases/Writing/DeleteWritingEntry/DeleteWritingEntryCommand.cs
using Mediator;

namespace MenuNest.Application.UseCases.Writing.DeleteWritingEntry;

/// <summary>
/// Soft-deletes a WritingEntry. Allowed even after a correction has locked
/// the text (entry-mutability / ADR-169) -- the lock only blocks edits.
/// </summary>
public sealed record DeleteWritingEntryCommand(Guid Id) : ICommand;
```

- [ ] **Step 4: Write the handler**

```csharp
// backend/src/MenuNest.Application/UseCases/Writing/DeleteWritingEntry/DeleteWritingEntryHandler.cs
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.DeleteWritingEntry;

public sealed class DeleteWritingEntryHandler : ICommandHandler<DeleteWritingEntryCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteWritingEntryHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteWritingEntryCommand command, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var entry = await _db.WritingEntries
            .FirstOrDefaultAsync(w => w.Id == command.Id && w.UserId == user.Id && w.DeletedAt == null, ct)
            ?? throw new DomainException("Writing entry not found.");

        entry.SoftDelete();
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter DeleteWritingEntryHandlerTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Writing/DeleteWritingEntry backend/tests/MenuNest.Application.UnitTests/Writing/DeleteWritingEntryHandlerTests.cs
git commit -m "feat(writing): add DeleteWritingEntry command + handler (#97)"
```

---

### Task 6: WebApi — extend `WritingEntriesController`

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Controllers/WritingEntriesController.cs`

**Interfaces:**
- Consumes: `ListWritingEntriesQuery` (Task 3), `UpdateWritingEntryTextCommand` (Task 4),
  `DeleteWritingEntryCommand` (Task 5).
- Produces: `GET /api/writing-entries`, `PUT /api/writing-entries/{id}`,
  `DELETE /api/writing-entries/{id}` — the exact routes the frontend (Task 7) calls. The existing
  `POST /api/writing-entries` route is unchanged (moved to a class-level `[Route]` attribute, same
  final URL).

- [ ] **Step 1: Rewrite the controller**

```csharp
// backend/src/MenuNest.WebApi/Controllers/WritingEntriesController.cs
using Mediator;
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.DeleteWritingEntry;
using MenuNest.Application.UseCases.Writing.ListWritingEntries;
using MenuNest.Application.UseCases.Writing.SubmitWritingEntry;
using MenuNest.Application.UseCases.Writing.UpdateWritingEntryText;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/writing-entries")]
public sealed class WritingEntriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public WritingEntriesController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Submits tonight's 7-minute freewrite entry. Marks the day "done" --
    /// no correction happens here (see docs/decision-map/writing-practice-build).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<WritingEntryDto>> Submit(
        [FromBody] SubmitWritingEntryCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lists every non-deleted entry for the current user, newest first --
    /// feeds the "ประวัติ" (History) screen.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WritingEntryDto>>> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListWritingEntriesQuery(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Edits an entry's text. Rejected once a correction has locked it
    /// (entry-mutability / ADR-169).
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WritingEntryDto>> UpdateText(
        Guid id,
        [FromBody] UpdateWritingEntryTextRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateWritingEntryTextCommand(id, request.Text), ct);
        return Ok(result);
    }

    /// <summary>
    /// Soft-deletes an entry -- allowed even when its text is locked.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteWritingEntryCommand(id), ct);
        return NoContent();
    }
}

public sealed record UpdateWritingEntryTextRequest(string Text);
```

- [ ] **Step 2: Build the backend to confirm it compiles**

Run: `dotnet build backend/MenuNest.sln` (check for a `.sln` file at `backend/` root first if the
name is wrong)
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the full backend test suite**

Run: `dotnet test backend/tests`
Expected: PASS — no test hard-codes the old `[HttpPost("api/writing-entries")]` attribute string
(confirmed: no WebApi-layer test references `WritingEntriesController` or `writing-entries` today),
so this refactor changes nothing observable.

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/WritingEntriesController.cs
git commit -m "feat(writing): expose GET/PUT/DELETE on /api/writing-entries (#97)"
```

---

### Task 7: Frontend — types + RTK Query endpoints

**Files:**
- Modify: `frontend/src/shared/api/writingTypes.ts`
- Modify: `frontend/src/shared/api/api.ts`

**Interfaces:**
- Consumes: nothing new (RTK Query `createApi` builder already wired; `'WritingEntries'` tag type
  already registered from Phase 1).
- Produces: `WritingEntryDto.correctedAt`, `UpdateWritingEntryTextRequest` type,
  `useListWritingEntriesQuery()`, `useUpdateWritingEntryTextMutation()`,
  `useDeleteWritingEntryMutation()` hooks — consumed by `WritingHistoryPage` (Task 8) and
  `WritingEntryDetailPage` (Task 9).

- [ ] **Step 1: Update the types file**

Replace the contents of `writingTypes.ts`:

```typescript
// frontend/src/shared/api/writingTypes.ts
export interface WritingEntryDto {
    id: string
    date: string // YYYY-MM-DD
    text: string
    elapsedSeconds: number
    wordsPerMinute: number
    correctedAt: string | null
    createdAt: string
}

export interface SubmitWritingEntryRequest {
    date: string // YYYY-MM-DD
    text: string
    elapsedSeconds: number
}

export interface UpdateWritingEntryTextRequest {
    text: string
}
```

- [ ] **Step 2: Update the import and add the three endpoints in `api.ts`**

Update the existing writing-types import line:

```typescript
import type {SubmitWritingEntryRequest, UpdateWritingEntryTextRequest, WritingEntryDto} from './writingTypes'
```

Replace the existing `// -------------------- Writing practice --------------------` block (which
currently holds only `submitWritingEntry`) with:

```typescript
        // -------------------- Writing practice --------------------
        submitWritingEntry: build.mutation<WritingEntryDto, SubmitWritingEntryRequest>({
            query: (body) => ({
                url: '/api/writing-entries',
                method: 'POST',
                body,
            }),
            invalidatesTags: [{type: 'WritingEntries', id: 'LIST'}],
        }),
        listWritingEntries: build.query<WritingEntryDto[], void>({
            query: () => '/api/writing-entries',
            providesTags: (result) =>
                result
                    ? [
                        ...result.map((e) => ({type: 'WritingEntries' as const, id: e.id})),
                        {type: 'WritingEntries', id: 'LIST'},
                    ]
                    : [{type: 'WritingEntries', id: 'LIST'}],
        }),
        updateWritingEntryText: build.mutation<WritingEntryDto, {id: string} & UpdateWritingEntryTextRequest>({
            query: ({id, ...body}) => ({
                url: `/api/writing-entries/${id}`,
                method: 'PUT',
                body,
            }),
            invalidatesTags: (_r, _e, a) => [
                {type: 'WritingEntries', id: a.id},
                {type: 'WritingEntries', id: 'LIST'},
            ],
        }),
        deleteWritingEntry: build.mutation<void, string>({
            query: (id) => ({url: `/api/writing-entries/${id}`, method: 'DELETE'}),
            invalidatesTags: (_r, _e, id) => [
                {type: 'WritingEntries', id},
                {type: 'WritingEntries', id: 'LIST'},
            ],
        }),
```

Add the three new hooks to the destructured export block at the bottom of `api.ts`, right after
`useSubmitWritingEntryMutation,`:

```typescript
    useListWritingEntriesQuery,
    useUpdateWritingEntryTextMutation,
    useDeleteWritingEntryMutation,
```

- [ ] **Step 3: Run the frontend typecheck**

Run: `cd frontend && npx tsc -b`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/shared/api/writingTypes.ts frontend/src/shared/api/api.ts
git commit -m "feat(writing): add list/update/delete RTK Query endpoints (#97)"
```

---

### Task 8: Frontend — `WritingHistoryPage` (Syncfusion Grid + filter chips + status badges)

**Files:**
- Create: `frontend/src/pages/writing/WritingHistoryPage.tsx`
- Create: `frontend/src/pages/writing/WritingHistoryPage.css`

**Interfaces:**
- Consumes: `useListWritingEntriesQuery` (Task 7), `Grid`/`Columns`/`Column`/`ColumnTemplateProps`
  from `@syncfusion/react-grid` (already a project dependency; its `material.css` is already
  imported in `frontend/src/main.tsx` — no dependency or CSS wiring needed).
- Produces: `WritingHistoryPage` component — wired into the router in Task 10. Row-click ("เปิด")
  navigates to `/writing/history/:id` (Task 9).

- [ ] **Step 1: Write the page component**

```tsx
// frontend/src/pages/writing/WritingHistoryPage.tsx
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Grid, Columns, Column, type ColumnTemplateProps } from '@syncfusion/react-grid'
import { useListWritingEntriesQuery } from '../../shared/api/api'
import type { WritingEntryDto } from '../../shared/api/writingTypes'
import './WritingHistoryPage.css'

type FilterMode = 'all' | 'pending' | 'corrected'

const stripHtml = (html: string): string =>
  html
    .replace(/<[^>]*>/g, ' ')
    .replace(/&nbsp;/gi, ' ')
    .replace(/\s+/g, ' ')
    .trim()

const formatDateThai = (iso: string): string =>
  new Date(iso).toLocaleDateString('th-TH', { day: 'numeric', month: 'short', year: 'numeric' })

function DateCell({ data }: ColumnTemplateProps<WritingEntryDto>) {
  return <span>{formatDateThai(data.date)}</span>
}

function TextPreviewCell({ data }: ColumnTemplateProps<WritingEntryDto>) {
  const preview = stripHtml(data.text)
  return <span className="writing-history-preview">{preview.length > 80 ? `${preview.slice(0, 80)}…` : preview}</span>
}

function StatusBadgeCell({ data }: ColumnTemplateProps<WritingEntryDto>) {
  return data.correctedAt ? (
    <span className="writing-history-badge writing-history-badge--corrected">🔒 ตรวจแล้ว</span>
  ) : (
    <span className="writing-history-badge writing-history-badge--pending">⏳ รอตรวจ</span>
  )
}

export function WritingHistoryPage() {
  const navigate = useNavigate()
  const { data: entries, isLoading, isError } = useListWritingEntriesQuery()
  const [filterMode, setFilterMode] = useState<FilterMode>('all')

  const pendingCount = useMemo(
    () => (entries ?? []).filter((e) => !e.correctedAt).length,
    [entries],
  )

  const rows = useMemo(() => {
    const list = entries ?? []
    if (filterMode === 'pending') return list.filter((e) => !e.correctedAt)
    if (filterMode === 'corrected') return list.filter((e) => e.correctedAt)
    return list
  }, [entries, filterMode])

  // OpenActionCell needs `navigate`, so it is declared inside the component
  // rather than as a module-level function like the other cell templates.
  function OpenActionCell({ data }: ColumnTemplateProps<WritingEntryDto>) {
    return (
      <button
        type="button"
        className="writing-history-open-btn"
        onClick={() => navigate(`/writing/history/${data.id}`)}
      >
        เปิด
      </button>
    )
  }

  return (
    <div className="writing-history-page">
      <button type="button" className="writing-history-back-btn" onClick={() => navigate('/writing')}>
        ← กลับ
      </button>
      <h1 className="writing-history-title">ประวัติ</h1>

      <div className="writing-history-filter-bar">
        <button
          type="button"
          className={
            filterMode === 'all' ? 'writing-history-chip writing-history-chip--active' : 'writing-history-chip'
          }
          onClick={() => setFilterMode('all')}
        >
          ทั้งหมด
        </button>
        <button
          type="button"
          className={
            filterMode === 'pending' ? 'writing-history-chip writing-history-chip--active' : 'writing-history-chip'
          }
          onClick={() => setFilterMode('pending')}
        >
          รอตรวจ{pendingCount > 0 ? ` (${pendingCount})` : ''}
        </button>
        <button
          type="button"
          className={
            filterMode === 'corrected' ? 'writing-history-chip writing-history-chip--active' : 'writing-history-chip'
          }
          onClick={() => setFilterMode('corrected')}
        >
          ตรวจแล้ว
        </button>
      </div>

      {isLoading && <div className="writing-history-status">กำลังโหลด...</div>}
      {isError && <div className="writing-history-status writing-history-status--error">โหลดไม่สำเร็จ</div>}
      {!isLoading && !isError && rows.length === 0 && (
        <div className="writing-history-status">ยังไม่มีรายการ</div>
      )}

      {!isLoading && !isError && rows.length > 0 && (
        <Grid dataSource={rows} pageSettings={{ enabled: true, pageSize: 20 }}>
          <Columns>
            <Column field="date" headerText="วันที่" width="110" template={DateCell} />
            <Column field="text" headerText="ข้อความ" template={TextPreviewCell} />
            <Column field="correctedAt" headerText="สถานะ" width="120" template={StatusBadgeCell} />
            <Column field="id" headerText="" width="80" template={OpenActionCell} />
          </Columns>
        </Grid>
      )}
    </div>
  )
}
```

The exact `Grid`/`Column`/`ColumnTemplateProps` API above was confirmed against the installed
`@syncfusion/react-grid` type declarations
(`frontend/node_modules/@syncfusion/react-grid/src/grid/types/column.interfaces.d.ts` and
`grid.interfaces.d.ts`) at plan-writing time. If a prop name has since changed with a package
update, re-check those files before adjusting the component.

- [ ] **Step 2: Write the CSS**

```css
/* frontend/src/pages/writing/WritingHistoryPage.css */
.writing-history-page {
  max-width: 640px;
  margin: 0 auto;
  padding: 16px;
}

.writing-history-back-btn {
  background: none;
  border: none;
  color: var(--color-text-muted);
  font-size: 13px;
  cursor: pointer;
  padding: 0;
  margin-bottom: 8px;
}

.writing-history-title {
  font-size: 20px;
  font-weight: 700;
  margin: 0 0 12px;
}

.writing-history-filter-bar {
  display: flex;
  gap: 8px;
  margin-bottom: 14px;
  flex-wrap: wrap;
}

.writing-history-chip {
  border: 1px solid var(--color-border);
  background: #fff;
  border-radius: 999px;
  padding: 6px 12px;
  font-size: 12px;
  cursor: pointer;
}

.writing-history-chip--active {
  background: var(--color-primary);
  border-color: var(--color-primary);
  color: #fff;
  font-weight: 600;
}

.writing-history-status {
  padding: 24px 0;
  text-align: center;
  color: var(--color-text-muted);
  font-size: 13px;
}

.writing-history-status--error {
  color: var(--color-danger);
}

.writing-history-badge {
  display: inline-block;
  font-size: 11px;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 999px;
  white-space: nowrap;
}

.writing-history-badge--pending {
  background: #fff3e0;
  color: #b26a00;
}

.writing-history-badge--corrected {
  background: #e6f4ea;
  color: #1e7e34;
}

.writing-history-preview {
  font-size: 12px;
  color: var(--color-text-muted);
}

.writing-history-open-btn {
  border: 1px solid var(--color-border);
  background: #fff;
  border-radius: 6px;
  padding: 4px 10px;
  font-size: 12px;
  cursor: pointer;
}
```

- [ ] **Step 3: Run the frontend typecheck and build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: 0 errors, build succeeds.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/writing/WritingHistoryPage.tsx frontend/src/pages/writing/WritingHistoryPage.css
git commit -m "feat(writing): add WritingHistoryPage with status badges + filter (#97)"
```

---

### Task 9: Frontend — `WritingEntryDetailPage` (view/edit/delete)

**Files:**
- Create: `frontend/src/pages/writing/WritingEntryDetailPage.tsx`
- Create: `frontend/src/pages/writing/WritingEntryDetailPage.css`

**Interfaces:**
- Consumes: `useListWritingEntriesQuery`, `useUpdateWritingEntryTextMutation`,
  `useDeleteWritingEntryMutation` (Task 7); `RichTextEditorComponent` + `Inject` + `Toolbar` +
  `Link` + `HtmlEditor` + `QuickToolbar` from `@syncfusion/ej2-react-richtexteditor` (already used
  by `WritingPage`).
- Produces: `WritingEntryDetailPage` component — wired into the router in Task 10.

This page selects its entry from the already-loaded `listWritingEntries` cache by `:id` rather than
adding a separate `GetWritingEntry` endpoint — the History screen always loads the full list first
(Task 8), so a second round trip for one row would be redundant (YAGNI); RTK Query's cache makes
this a synchronous lookup once the list query has resolved.

- [ ] **Step 1: Write the page component**

```tsx
// frontend/src/pages/writing/WritingEntryDetailPage.tsx
import { useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  RichTextEditorComponent,
  Inject,
  Toolbar,
  Link,
  HtmlEditor,
  QuickToolbar,
  type RichTextEditorComponent as RteInstance,
} from '@syncfusion/ej2-react-richtexteditor'
import {
  useListWritingEntriesQuery,
  useUpdateWritingEntryTextMutation,
  useDeleteWritingEntryMutation,
} from '../../shared/api/api'
import './WritingEntryDetailPage.css'

const formatDateThai = (iso: string): string =>
  new Date(iso).toLocaleDateString('th-TH', { day: 'numeric', month: 'short', year: 'numeric' })

export function WritingEntryDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: entries, isLoading } = useListWritingEntriesQuery()
  const [updateText, { isLoading: isSaving }] = useUpdateWritingEntryTextMutation()
  const [deleteEntry, { isLoading: isDeleting }] = useDeleteWritingEntryMutation()
  const rteRef = useRef<RteInstance | null>(null)
  const [isEditing, setIsEditing] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const entry = useMemo(() => entries?.find((e) => e.id === id), [entries, id])
  const isLocked = Boolean(entry?.correctedAt)

  const handleSave = async () => {
    if (!entry) return
    const html = rteRef.current?.getHtml() ?? ''
    setError(null)
    try {
      await updateText({ id: entry.id, text: html }).unwrap()
      setIsEditing(false)
    } catch (err) {
      console.error('updateWritingEntryText failed', err)
      setError('บันทึกไม่สำเร็จ ลองอีกครั้ง')
    }
  }

  const handleDelete = async () => {
    if (!entry) return
    setError(null)
    try {
      await deleteEntry(entry.id).unwrap()
      navigate('/writing/history')
    } catch (err) {
      console.error('deleteWritingEntry failed', err)
      setError('ลบไม่สำเร็จ ลองอีกครั้ง')
    }
  }

  if (isLoading) {
    return <div className="writing-detail-page writing-detail-status">กำลังโหลด...</div>
  }

  if (!entry) {
    return (
      <div className="writing-detail-page">
        <button type="button" className="writing-detail-back-btn" onClick={() => navigate('/writing/history')}>
          ← กลับ
        </button>
        <div className="writing-detail-status">ไม่พบรายการนี้ (อาจถูกลบไปแล้ว)</div>
      </div>
    )
  }

  return (
    <div className="writing-detail-page">
      <button type="button" className="writing-detail-back-btn" onClick={() => navigate('/writing/history')}>
        ← กลับ
      </button>

      <div className="writing-detail-header">
        <span className="writing-detail-date">{formatDateThai(entry.date)}</span>
        <span
          className={
            isLocked
              ? 'writing-history-badge writing-history-badge--corrected'
              : 'writing-history-badge writing-history-badge--pending'
          }
        >
          {isLocked ? '🔒 ตรวจแล้ว' : '⏳ รอตรวจ'}
        </span>
      </div>

      {isEditing ? (
        <RichTextEditorComponent ref={rteRef} height={300} value={entry.text}>
          <Inject services={[Toolbar, Link, HtmlEditor, QuickToolbar]} />
        </RichTextEditorComponent>
      ) : (
        // Trusted content: this HTML is the signed-in user's own writing,
        // authored by the same Syncfusion RTE that produced it (WritingPage) --
        // no third party ever supplies this string.
        <div className="writing-detail-text" dangerouslySetInnerHTML={{ __html: entry.text }} />
      )}

      {error && <div className="writing-detail-error">{error}</div>}

      <div className="writing-detail-actions">
        {isLocked ? (
          <div className="writing-detail-locked-note">ตรวจแล้ว — แก้ข้อความไม่ได้ (ลบทั้งรายการได้)</div>
        ) : isEditing ? (
          <>
            <button type="button" className="writing-detail-save-btn" onClick={handleSave} disabled={isSaving}>
              บันทึก
            </button>
            <button type="button" className="writing-detail-cancel-btn" onClick={() => setIsEditing(false)}>
              ยกเลิก
            </button>
          </>
        ) : (
          <button type="button" className="writing-detail-edit-btn" onClick={() => setIsEditing(true)}>
            แก้ไข
          </button>
        )}

        {confirmingDelete ? (
          <span className="writing-detail-confirm-delete">
            ลบรายการนี้แน่ใจไหม?
            <button type="button" className="writing-detail-confirm-yes" onClick={handleDelete} disabled={isDeleting}>
              ลบ
            </button>
            <button type="button" className="writing-detail-confirm-no" onClick={() => setConfirmingDelete(false)}>
              ยกเลิก
            </button>
          </span>
        ) : (
          <button type="button" className="writing-detail-delete-btn" onClick={() => setConfirmingDelete(true)}>
            ลบ
          </button>
        )}
      </div>
    </div>
  )
}
```

The `RichTextEditorComponent`'s `value` prop (used here to preload existing text into the editor)
is declared on the underlying `@syncfusion/ej2-richtexteditor` base class
(`frontend/node_modules/@syncfusion/ej2-richtexteditor/src/rich-text-editor/base/rich-text-editor.d.ts`,
`value: string`) and is exposed the same way `height`/`placeholder`/`toolbarSettings` already are in
`WritingPage.tsx` — confirm it renders the existing text once Step 1 is in place; if it does not,
call `rteRef.current?.refreshUI?.()` or set the value imperatively via the ref instead (check the
same base file for the imperative setter) rather than guessing further.

- [ ] **Step 2: Write the CSS**

```css
/* frontend/src/pages/writing/WritingEntryDetailPage.css */
.writing-detail-page {
  max-width: 480px;
  margin: 0 auto;
  padding: 16px;
}

.writing-detail-back-btn {
  background: none;
  border: none;
  color: var(--color-text-muted);
  font-size: 13px;
  cursor: pointer;
  padding: 0;
  margin-bottom: 8px;
}

.writing-detail-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
}

.writing-detail-date {
  font-size: 14px;
  font-weight: 600;
}

.writing-history-badge {
  display: inline-block;
  font-size: 11px;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 999px;
  white-space: nowrap;
}

.writing-history-badge--pending {
  background: #fff3e0;
  color: #b26a00;
}

.writing-history-badge--corrected {
  background: #e6f4ea;
  color: #1e7e34;
}

.writing-detail-text {
  border: 1px solid var(--color-border);
  border-radius: 10px;
  padding: 12px;
  font-size: 13px;
  line-height: 1.6;
  min-height: 120px;
}

.writing-detail-status,
.writing-detail-error {
  padding: 16px 0;
  text-align: center;
  font-size: 13px;
}

.writing-detail-error {
  color: var(--color-danger);
}

.writing-detail-actions {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 14px;
  flex-wrap: wrap;
}

.writing-detail-save-btn,
.writing-detail-edit-btn {
  background: var(--color-primary);
  color: #fff;
  border: none;
  border-radius: 8px;
  padding: 8px 16px;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
}

.writing-detail-cancel-btn,
.writing-detail-delete-btn,
.writing-detail-confirm-no {
  background: #fff;
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 8px 16px;
  font-size: 13px;
  cursor: pointer;
}

.writing-detail-delete-btn {
  color: var(--color-danger);
  border-color: var(--color-danger);
}

.writing-detail-confirm-delete {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--color-danger);
}

.writing-detail-confirm-yes {
  background: var(--color-danger);
  color: #fff;
  border: none;
  border-radius: 6px;
  padding: 6px 10px;
  font-size: 12px;
  cursor: pointer;
}

.writing-detail-locked-note {
  font-size: 12px;
  color: var(--color-text-muted);
}
```

- [ ] **Step 3: Run the frontend typecheck and build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: 0 errors, build succeeds.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/writing/WritingEntryDetailPage.tsx frontend/src/pages/writing/WritingEntryDetailPage.css
git commit -m "feat(writing): add WritingEntryDetailPage with edit/delete (#97)"
```

---

### Task 10: Wire routing + link from the เขียน page

**Files:**
- Modify: `frontend/src/pages/writing/index.ts`
- Modify: `frontend/src/router.tsx`
- Modify: `frontend/src/pages/writing/WritingPage.tsx`
- Modify: `frontend/src/pages/writing/WritingPage.css`

**Interfaces:**
- Consumes: `WritingHistoryPage` (Task 8), `WritingEntryDetailPage` (Task 9).
- Produces: routes `/writing/history` and `/writing/history/:id`, reachable from a "ดูประวัติทั้งหมด"
  link on the เขียน page — no new nav item (mirrors `HealthHomePage`'s link to `/health/history`;
  `NavBar.tsx` is not touched by this plan).

- [ ] **Step 1: Update the barrel export**

Replace the contents of `frontend/src/pages/writing/index.ts`:

```typescript
export { WritingPage } from './WritingPage'
export { WritingHistoryPage } from './WritingHistoryPage'
export { WritingEntryDetailPage } from './WritingEntryDetailPage'
```

- [ ] **Step 2: Add the routes**

In `router.tsx`, update the writing import line:

```typescript
import { WritingPage, WritingHistoryPage, WritingEntryDetailPage } from './pages/writing'
```

Add the two new routes right after the existing `/writing` line (same `AppLayout` children array,
no family gate — matches the existing `/writing` route's placement):

```typescript
          { path: '/writing', element: <WritingPage /> },
          { path: '/writing/history', element: <WritingHistoryPage /> },
          { path: '/writing/history/:id', element: <WritingEntryDetailPage /> },
```

- [ ] **Step 3: Add the link on the เขียน page**

In `WritingPage.tsx`, add the import next to the existing `react-router-dom`-adjacent imports (note:
`Link` is **not** imported from `react-router-dom` here — it would collide with the `Link` toolbar
service already imported from `@syncfusion/ej2-react-richtexteditor` on the line above. Use
`useNavigate` + a plain button instead, matching `HealthHomePage`'s own
`onClick={() => navigate('/health/history')}` pattern for its "📜 ดูประวัติทั้งหมด" link):

```typescript
import { useNavigate } from 'react-router-dom'
```

Add the hook call inside the component, right after the existing hook calls:

```typescript
  const navigate = useNavigate()
```

Add the button at the end of the JSX, right after the `writing-correction-note` div:

```tsx
      <button
        type="button"
        className="writing-history-link-btn"
        onClick={() => navigate('/writing/history')}
      >
        📜 ดูประวัติทั้งหมด
      </button>
```

- [ ] **Step 4: Add the CSS for the new button**

Append to `WritingPage.css`:

```css
.writing-history-link-btn {
  display: block;
  width: 100%;
  margin-top: 10px;
  padding: 10px;
  border: 1px solid var(--color-border);
  border-radius: 10px;
  background: #fff;
  color: var(--color-text-muted);
  font-size: 12px;
  text-align: center;
  cursor: pointer;
}
```

- [ ] **Step 5: Run the frontend typecheck and build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: 0 errors, build succeeds.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/writing/index.ts frontend/src/router.tsx frontend/src/pages/writing/WritingPage.tsx frontend/src/pages/writing/WritingPage.css
git commit -m "feat(writing): wire the History screen into routing (#97)"
```

- [ ] **Step 7: Interactive verification (required — CLAUDE.md, no jsdom in this repo)**

Run the app (`npm run dev` in `frontend`, backend running locally), sign in, and check against the
shape confirmed on the `pending-correction-visibility` ticket:

1. From `/writing`, tap "📜 ดูประวัติทั้งหมด" — confirm it opens `/writing/history`.
2. Confirm the History screen lists every past entry as a grid row: date, a stripped/truncated text
   preview, a status badge (⏳ รอตรวจ or 🔒 ตรวจแล้ว — every entry today reads ⏳ รอตรวจ, since no
   correction path exists yet), and an "เปิด" button per row.
3. Tap each of the three filter chips (ทั้งหมด / รอตรวจ / ตรวจแล้ว) — confirm the grid narrows to
   the matching rows, and the "รอตรวจ (N)" count matches the number of ⏳ rows shown under "ทั้งหมด".
4. Tap "เปิด" on a row — confirm it opens `/writing/history/:id` showing the full text, the same
   status badge, and (since nothing is corrected yet) an "แก้ไข" button.
5. Tap "แก้ไข" — confirm the Rich Text Editor opens pre-filled with the existing text and its
   toolbar works; edit the text and tap "บันทึก" — confirm it saves and the detail view shows the
   new text.
6. Tap "ลบ" — confirm the inline "ลบรายการนี้แน่ใจไหม?" confirm row appears; tap "ลบ" again — confirm
   it navigates back to `/writing/history` and the entry no longer appears in any filter view.
7. Confirm a freshly-submitted entry (submit one from `/writing` if none is fresh) appears at the
   top of the History list (newest first) with ⏳ รอตรวจ.

Fix anything found before considering this plan complete — do not defer interactive verification.

---

### Task 11: Apply the migration to prod, verify, close the loop

**Files:** none (operational step).

- [ ] **Step 1: Apply the EF migration to the prod database by hand**

Per `CLAUDE.md`'s "Database migrations are applied MANUALLY" section — from `backend/`, with the
terminal `az` session as `personal@example.com` (add a temporary SQL firewall rule first if
the current IP is rejected, per the same section):

```bash
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```

Confirm the `WritingEntries` table now has a `DeletedAt` column (e.g. `SELECT TOP 1 DeletedAt FROM
WritingEntries` via the same connection, or check `dotnet ef migrations list` reports
`AddWritingEntryDeletedAt` as applied).

- [ ] **Step 2: Smoke-test on prod**

After the next deploy (push to `main` per `CLAUDE.md`), repeat Task 10 Step 7's interactive check
against the deployed app, not just localhost.

- [ ] **Step 3: Update the decision map / project memory**

Both `entry-mutability` and `pending-correction-visibility` are already `closed` on
`docs/decision-map/writing-practice-build` — no ticket status changes here. Once verified on prod,
tell the user the History screen is built, and that the map's remaining fog (draft
autosave/crash-recovery; restoring a soft-deleted entry) and the progress screen / MCP correction
tools are still separate, unstarted work.

---

## Self-Review

**Spec coverage:** `entry-mutability`/ADR-169 — full CRUD via a new History screen (Tasks 6–9); a
correction locks text, edit throws `DomainException` while an entry is still deletable when locked
(Task 1, tested in Task 1 + Task 5's `Allows_deleting_an_already_corrected_locked_entry`); delete is
soft, row stays for the monthly comparison, every query filters `DeletedAt == null` (Tasks 1–5) —
all OK. `pending-correction-visibility` — per-row status badge + tappable filter inside the History
screen, built as a Syncfusion `react-grid` DataGrid, no nav badge (Task 8) — OK. Three-DbContext
rule — no new DbSet needed, already wired from Phase 1 (noted in Global Constraints, verified
against the current repo state before writing this plan) — OK. Issue #97 referenced in every commit
— OK. Interactive verification required before done (Task 10 Step 7, Task 11 Step 2) — OK.

**Placeholder scan:** no TBD/TODO. Two explicitly-flagged "confirm against the installed
types/behavior" notes (Task 8 Step 1's Grid API, Task 9 Step 1's RTE `value` prop) are deliberate —
both were already checked against the installed packages' `.d.ts` files at plan-writing time (unlike
the Phase 1 plan's two guesses), and the note exists only in case a package update has since moved
something.

**Type consistency:** `WritingEntryDto` — Domain entity (`WritingEntry.Id/Date/Text/ElapsedSeconds/
WordsPerMinute/CorrectedAt/CreatedAt`, Task 1) → DTO (Task 3 Step 1) → `SubmitWritingEntryHandler`,
`ListWritingEntriesHandler`, `UpdateWritingEntryTextHandler` (Tasks 3–4) → controller (Task 6) →
frontend `WritingEntryDto` (Task 7 Step 1, `correctedAt: string | null` matching the DTO's
`DateTime?`) — names and shapes match end-to-end. `UpdateWritingEntryTextCommand(Id, Text)` matches
`UpdateWritingEntryTextRequest(Text)` + the controller's own route-bound `id` (Task 6). Frontend
mutation hook parameter shapes (`{id: string} & UpdateWritingEntryTextRequest`, Task 7) match how
`WritingEntryDetailPage` calls them (Task 9: `updateText({id: entry.id, text: html})`,
`deleteEntry(entry.id)`).
