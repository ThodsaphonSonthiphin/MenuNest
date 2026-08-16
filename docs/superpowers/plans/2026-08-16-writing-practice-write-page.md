# Writing Practice — Write Page (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use sp-subagent-driven-development (recommended) or sp-executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the "เขียน" (write) page of MenuNest's writing-practice feature — a 7-minute
freewrite timer, a Syncfusion Rich Text Editor field, a submit action, and storage — so night 1
of the habit can start today.

**Architecture:** Standard MenuNest CQRS slice: a `WritingEntry` domain entity (factory-method,
private setters), one `Mediator` command (`SubmitWritingEntryCommand`) + handler + FluentValidation
validator in `MenuNest.Application`, a thin `WritingEntriesController` in `MenuNest.WebApi`, EF Core
persistence via the existing `IApplicationDbContext` (three implementers), and a new React page
(`WritingPage`) wired into the existing router/nav — mirroring the `Intake` (Health module) and
`Trip` (top-level, user-scoped, no Family gate) patterns already in the codebase.

**Tech Stack:** .NET 10 / EF Core / `Mediator` (source-gen CQRS) / FluentValidation / xUnit + Moq +
FluentAssertions — React 19 / TypeScript / RTK Query / `@syncfusion/react-buttons` /
`@syncfusion/ej2-react-richtexteditor@33.1.49` (new dependency, pinned to match the repo's other
`ej2-*` packages) / vitest (pure-logic only, per `CLAUDE.md` — no jsdom in this repo).

**Spec:** `docs/decision-map/writing-practice-build/` (tickets: `timer-resilience`,
`done-day-redefinition`, `one-tap-access`, and the RTE choice recorded in
[[project-writing-practice-build]] memory / this session). UI mockup: Claude Design project
"MenuNest design system" (personal account, projectId `107862ef-c14b-42f4-a8f2-4bbe36951e25`),
card `screens/writing-practice.html`, the "เขียน" frame.

## Global Constraints

- Every commit references issue **#97** (`(#97)` in the subject, or `Refs #97` in the body — this
  plan does not close it, since Phase 2/3 remain).
- `git add` **explicit paths only** — never `-A` / `.` (project rule).
- The pre-commit hook runs the **full** backend + frontend suite; every commit must leave it green.
- New `DbSet<WritingEntry>` must be added to **all three** `IApplicationDbContext` implementers
  (`AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext`) in the **same** commit as the
  entity — an unmapped/missing DbSet fails EF model validation for every test touching the context.
- **Do not build correction UI, the progress screen, or MCP tools in this plan.** Those are
  Phase 2/3, tracked on `pending-correction-visibility` and the already-resolved
  `mcp-tool-contract` ticket. The entity schema below reserves nullable columns for them so Phase 2
  needs no second migration — but nothing populates those columns here.
- **Timer is wall-clock, never paused** (`timer-resilience`): compute remaining time from a stored
  start timestamp on every tick; do not pause on `visibilitychange` or tab blur.
- **Done = submit after the timer finishes** (`done-day-redefinition`) — there is no correction
  step in this page.
- Ship as a **normal page in the existing nav** (`one-tap-access`) — no PWA shortcut, no special
  routing.
- The prod SQL migration is applied **by hand** after merge — see Task 10's final step, and
  `CLAUDE.md`'s "Database migrations are applied MANUALLY" section for the exact command
  (`AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update ...`).

---

### Task 1: `WritingEntry` domain entity

**Files:**
- Create: `backend/src/MenuNest.Domain/Entities/WritingEntry.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Writing/WritingEntryTests.cs`

**Interfaces:**
- Consumes: `MenuNest.Domain.Common.Entity` (base class — `Id`, `CreatedAt`, `UpdatedAt`),
  `MenuNest.Domain.Exceptions.DomainException`.
- Produces: `WritingEntry` with public factory `WritingEntry.Create(Guid userId, DateOnly date,
  string text, int elapsedSeconds)`, and read-only properties: `UserId` (Guid), `Date` (DateOnly),
  `Text` (string), `ElapsedSeconds` (int), `WordsPerMinute` (double). Plus Phase-2 reserved
  properties, all nullable and unset by `Create`: `CorrectedAt` (DateTime?), `TargetRule`
  (string?), `HitCount` (int?), `MissCount` (int?), `ThaiWhyLine` (string?),
  `SentenceCombiningItemsJson` (string?), `StuckWordsJson` (string?).

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Writing;

public class WritingEntryTests
{
    private static readonly DateOnly Today = new(2026, 8, 16);

    [Fact]
    public void Create_computes_words_per_minute_from_stripped_text_and_elapsed_seconds()
    {
        var userId = Guid.NewGuid();
        // 10 words of visible text, 7 minutes (420s) elapsed -> ~1.4286 wpm.
        var entry = WritingEntry.Create(
            userId,
            Today,
            "<p>one two three four five six seven eight nine ten</p>",
            elapsedSeconds: 420);

        entry.UserId.Should().Be(userId);
        entry.Date.Should().Be(Today);
        entry.Text.Should().Be("<p>one two three four five six seven eight nine ten</p>");
        entry.ElapsedSeconds.Should().Be(420);
        entry.WordsPerMinute.Should().BeApproximately(10.0 / 7.0, 0.001);
        entry.CorrectedAt.Should().BeNull();
        entry.TargetRule.Should().BeNull();
    }

    [Fact]
    public void Create_throws_when_user_id_is_empty()
    {
        var act = () => WritingEntry.Create(Guid.Empty, Today, "<p>hi there</p>", 60);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_throws_when_text_is_empty_or_whitespace_only_html()
    {
        var act = () => WritingEntry.Create(Guid.NewGuid(), Today, "<p>   </p>", 60);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_throws_when_elapsed_seconds_is_not_positive()
    {
        var act = () => WritingEntry.Create(Guid.NewGuid(), Today, "<p>hi there</p>", 0);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Words_per_minute_is_zero_when_stripped_text_has_no_words()
    {
        // HTML-only content (e.g. an empty formatted paragraph) with no visible
        // words would divide-by-zero if not guarded — assert it does not throw
        // and instead the earlier "empty text" guard rejects it first, OR (if
        // some non-empty-but-wordless edge case exists) WordsPerMinute is 0,
        // never NaN/Infinity.
        var entry = WritingEntry.Create(Guid.NewGuid(), Today, "<p>a</p>", 60);
        double.IsNaN(entry.WordsPerMinute).Should().BeFalse();
        double.IsInfinity(entry.WordsPerMinute).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter WritingEntryTests`
Expected: FAIL — `WritingEntry` does not exist (CS0246).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text.RegularExpressions;
using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// One night's 7-minute freewrite entry (issue #97, Phase 1). Storage-only
/// today — the AI correction fields below are reserved for Phase 2
/// (WritingTools MCP, ai-correction-invocation) and stay null until that
/// phase lands, so no second migration is needed then.
/// </summary>
public sealed partial class WritingEntry : Entity
{
    public Guid UserId { get; private set; }
    public DateOnly Date { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public int ElapsedSeconds { get; private set; }
    public double WordsPerMinute { get; private set; }

    // Phase 2 (mcp-tool-contract's record_writing_correction) — reserved, unset here.
    public DateTime? CorrectedAt { get; private set; }
    public string? TargetRule { get; private set; }
    public int? HitCount { get; private set; }
    public int? MissCount { get; private set; }
    public string? ThaiWhyLine { get; private set; }
    public string? SentenceCombiningItemsJson { get; private set; }
    public string? StuckWordsJson { get; private set; }

    // EF Core
    private WritingEntry() { }

    public static WritingEntry Create(
        Guid userId,
        DateOnly date,
        string text,
        int elapsedSeconds)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");
        if (elapsedSeconds <= 0)
            throw new DomainException("ElapsedSeconds must be positive.");

        var wordCount = CountWords(text);
        if (wordCount == 0)
            throw new DomainException("Text must contain at least one word.");

        var minutes = elapsedSeconds / 60.0;

        return new WritingEntry
        {
            UserId = userId,
            Date = date,
            Text = text,
            ElapsedSeconds = elapsedSeconds,
            WordsPerMinute = minutes > 0 ? wordCount / minutes : 0
        };
    }

    /// <summary>
    /// Approximate word count of RTE-produced HTML: strips tags, collapses
    /// whitespace, splits on spaces. Good enough for a words-per-minute
    /// signal — not a precise linguistic tokenizer.
    /// </summary>
    private static int CountWords(string html)
    {
        var stripped = TagRegex().Replace(html, " ");
        var normalized = stripped.Trim();
        if (normalized.Length == 0) return 0;
        return WhitespaceRegex().Split(normalized).Length;
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter WritingEntryTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/WritingEntry.cs backend/tests/MenuNest.Application.UnitTests/Writing/WritingEntryTests.cs
git commit -m "feat(writing): add WritingEntry domain entity (#97)"
```

---

### Task 2: Persistence — DbSets, EF configuration, migration

**Files:**
- Modify: `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/WritingEntryConfiguration.cs`
- Create (generated): `backend/src/MenuNest.Infrastructure/Persistence/Migrations/*_AddWritingEntries.cs`
  (+ `.Designer.cs`, regenerated `AppDbContextModelSnapshot.cs`)
- Test: `backend/tests/MenuNest.Infrastructure.IntegrationTests/Persistence/WritingEntryConfigurationTests.cs`

**Interfaces:**
- Consumes: `WritingEntry` from Task 1.
- Produces: `IApplicationDbContext.WritingEntries` (`DbSet<WritingEntry>`), available to Task 3's
  handler and to Phase 2's MCP tools later.

- [ ] **Step 1: Add the DbSet to the interface and all three implementers**

`IApplicationDbContext.cs` — add next to the other `DbSet<>` properties:

```csharp
    DbSet<WritingEntry> WritingEntries { get; }
```

`AppDbContext.cs` — add next to `Intakes`:

```csharp
    public DbSet<WritingEntry> WritingEntries => Set<WritingEntry>();
```

`InMemoryAppDbContext.cs` and `SqliteAppDbContext.cs` — same line, added next to their own
`Intakes` DbSet.

- [ ] **Step 2: Write the EF configuration**

```csharp
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class WritingEntryConfiguration : IEntityTypeConfiguration<WritingEntry>
{
    public void Configure(EntityTypeBuilder<WritingEntry> builder)
    {
        builder.ToTable("WritingEntries");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.UserId).IsRequired();
        builder.Property(w => w.Date).IsRequired();
        builder.Property(w => w.Text).IsRequired();
        builder.Property(w => w.ElapsedSeconds).IsRequired();
        builder.Property(w => w.WordsPerMinute).IsRequired();

        // Phase 2 (record_writing_correction) — nullable, unpopulated in Phase 1.
        builder.Property(w => w.TargetRule).HasMaxLength(200);
        builder.Property(w => w.ThaiWhyLine).HasMaxLength(2000);

        // Hot query for Phase 2's list_pending_writing_entries (CorrectedAt IS NULL)
        // and for a future "my entries" list — both filter/sort by user + date.
        builder.HasIndex(w => new { w.UserId, w.Date });

        // Same NoAction rationale as Trip/Intake's User FK (see TripConfiguration,
        // IntakeConfiguration): avoids SQL Server's multi-cascade-path rejection
        // across the User's other relationships.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
```

- [ ] **Step 3: Write a relational config test (SqliteAppDbContext)**

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.IntegrationTests.Persistence;

public class WritingEntryConfigurationTests
{
    [Fact]
    public async Task Round_trips_a_writing_entry_through_sqlite()
    {
        using var db = SqliteAppDbContext.CreateOpen();

        var user = User.CreateFromExternalLogin(
            externalId: "wp-test-oid",
            email: "wp@example.com",
            displayName: "WP Test",
            authProvider: AuthProvider.Microsoft);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = WritingEntry.Create(
            user.Id,
            new DateOnly(2026, 8, 16),
            "<p>my daughter play with her toy</p>",
            elapsedSeconds: 420);
        db.WritingEntries.Add(entry);
        await db.SaveChangesAsync();

        var reloaded = await db.WritingEntries.SingleAsync(w => w.Id == entry.Id);
        reloaded.UserId.Should().Be(user.Id);
        reloaded.Date.Should().Be(new DateOnly(2026, 8, 16));
        reloaded.ElapsedSeconds.Should().Be(420);
        reloaded.CorrectedAt.Should().BeNull();
    }
}
```

Check `SqliteAppDbContext`'s actual construction helper name (`CreateOpen()` above is a guess at
the convention — read `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs`
in full first and match its real factory method / constructor signature before writing this test).

- [ ] **Step 4: Run test to verify it fails (entity/config not yet wired)**

Run: `dotnet test backend/tests/MenuNest.Infrastructure.IntegrationTests --filter WritingEntryConfigurationTests`
Expected: FAIL to compile until Steps 1–2 are in place — do Steps 1–2 first if working strictly
red-green, or treat Steps 1–3 as one unit here since they are all additive plumbing with no
pre-existing green state to break.

- [ ] **Step 5: Generate the migration**

```bash
cd backend
dotnet ef migrations add AddWritingEntries --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Inspect the generated migration's `Up()` — confirm it creates table `WritingEntries` with columns
matching Step 2 exactly (including the nullable Phase-2 columns) and the `(UserId, Date)` index.

- [ ] **Step 6: Run the full backend test suite**

Run: `dotnet test backend/tests` (or run each of the 4 test projects)
Expected: PASS, including `WritingEntryConfigurationTests`.

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs backend/src/MenuNest.Infrastructure/Persistence/Configurations/WritingEntryConfiguration.cs backend/src/MenuNest.Infrastructure/Persistence/Migrations backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs backend/tests/MenuNest.Infrastructure.IntegrationTests/Persistence/WritingEntryConfigurationTests.cs
git commit -m "feat(writing): persist WritingEntry via EF Core, all three DbContexts (#97)"
```

---

### Task 3: Application layer — `SubmitWritingEntry` command, handler, validator, DTO

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Writing/SubmitWritingEntry/SubmitWritingEntryCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Writing/SubmitWritingEntry/SubmitWritingEntryHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Writing/SubmitWritingEntry/SubmitWritingEntryValidator.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Writing/SubmitWritingEntryHandlerTests.cs`

**Interfaces:**
- Consumes: `WritingEntry.Create(...)` (Task 1), `IApplicationDbContext.WritingEntries` (Task 2),
  `IUserProvisioner.GetOrProvisionCurrentAsync(ct)`.
- Produces: `SubmitWritingEntryCommand(DateOnly Date, string Text, int ElapsedSeconds) :
  ICommand<WritingEntryDto>`, `WritingEntryDto(Guid Id, DateOnly Date, string Text, int
  ElapsedSeconds, double WordsPerMinute, DateTime CreatedAt)` — the exact shape
  `WritingEntriesController` (Task 4) and the frontend (Task 5) consume.

- [ ] **Step 1: Write the DTO**

```csharp
namespace MenuNest.Application.UseCases.Writing;

/// <summary>
/// A submitted writing-practice entry — returned after
/// <c>POST /api/writing-entries</c>.
/// </summary>
public sealed record WritingEntryDto(
    Guid Id,
    DateOnly Date,
    string Text,
    int ElapsedSeconds,
    double WordsPerMinute,
    DateTime CreatedAt);
```

- [ ] **Step 2: Write the command**

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Writing.SubmitWritingEntry;

/// <summary>
/// Submits tonight's 7-minute freewrite entry. Per done-day-redefinition
/// (docs/decision-map/writing-practice-build), this alone marks the day
/// "done" — no correction step happens here.
/// </summary>
public sealed record SubmitWritingEntryCommand(
    DateOnly Date,
    string Text,
    int ElapsedSeconds) : ICommand<WritingEntryDto>;
```

- [ ] **Step 3: Write the failing handler test**

```csharp
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.SubmitWritingEntry;

namespace MenuNest.Application.UnitTests.Writing;

public class SubmitWritingEntryHandlerTests
{
    private static readonly DateTime FixedNow =
        new(2026, 08, 16, 22, 30, 00, DateTimeKind.Utc);

    private static SubmitWritingEntryHandler Build(HandlerTestFixture fx, FixedClock clock)
        => new(fx.Db, fx.UserProvisioner.Object, new SubmitWritingEntryValidator(), clock);

    [Fact]
    public async Task Creates_entry_scoped_to_current_user_with_computed_words_per_minute()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var result = await Build(fx, clock).Handle(
            new SubmitWritingEntryCommand(
                Date: new DateOnly(2026, 8, 16),
                Text: "<p>my daughter play with her toy all morning</p>",
                ElapsedSeconds: 420),
            CancellationToken.None);

        result.Date.Should().Be(new DateOnly(2026, 8, 16));
        result.ElapsedSeconds.Should().Be(420);
        result.WordsPerMinute.Should().BeApproximately(9.0 / 7.0, 0.001);

        var stored = fx.Db.WritingEntries.Single();
        stored.UserId.Should().Be(fx.User.Id);
        stored.CorrectedAt.Should().BeNull();
    }

    [Fact]
    public async Task Validator_rejects_empty_text()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var act = async () => await Build(fx, clock).Handle(
            new SubmitWritingEntryCommand(new DateOnly(2026, 8, 16), "", 420),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Validator_rejects_non_positive_elapsed_seconds()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var act = async () => await Build(fx, clock).Handle(
            new SubmitWritingEntryCommand(new DateOnly(2026, 8, 16), "<p>hi there today</p>", 0),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Two_entries_same_user_same_day_are_both_allowed()
    {
        // No uniqueness decision was made for (UserId, Date) — a return-night
        // rewrite or a second sitting should not be silently blocked.
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var handler = Build(fx, clock);

        await handler.Handle(
            new SubmitWritingEntryCommand(new DateOnly(2026, 8, 16), "<p>first entry today</p>", 420),
            CancellationToken.None);
        await handler.Handle(
            new SubmitWritingEntryCommand(new DateOnly(2026, 8, 16), "<p>second entry today</p>", 420),
            CancellationToken.None);

        fx.Db.WritingEntries.Count().Should().Be(2);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter SubmitWritingEntryHandlerTests`
Expected: FAIL — handler/validator do not exist yet.

- [ ] **Step 5: Write the validator**

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.SubmitWritingEntry;

public sealed class SubmitWritingEntryValidator : AbstractValidator<SubmitWritingEntryCommand>
{
    public SubmitWritingEntryValidator()
    {
        RuleFor(x => x.Text).NotEmpty();
        RuleFor(x => x.ElapsedSeconds).GreaterThan(0);
        // A generous ceiling — guards against a garbage/runaway client value
        // without encoding any real product rule about session length.
        RuleFor(x => x.ElapsedSeconds).LessThanOrEqualTo(3600);
    }
}
```

- [ ] **Step 6: Write the handler**

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.SubmitWritingEntry;

public sealed class SubmitWritingEntryHandler : ICommandHandler<SubmitWritingEntryCommand, WritingEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<SubmitWritingEntryCommand> _validator;
    private readonly IClock _clock;

    public SubmitWritingEntryHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<SubmitWritingEntryCommand> validator,
        IClock clock)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
        _clock = clock;
    }

    public async ValueTask<WritingEntryDto> Handle(SubmitWritingEntryCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var entry = WritingEntry.Create(
            userId: user.Id,
            date: command.Date,
            text: command.Text,
            elapsedSeconds: command.ElapsedSeconds);

        _db.WritingEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        return new WritingEntryDto(
            Id: entry.Id,
            Date: entry.Date,
            Text: entry.Text,
            ElapsedSeconds: entry.ElapsedSeconds,
            WordsPerMinute: entry.WordsPerMinute,
            CreatedAt: entry.CreatedAt);
    }
}
```

(`_clock` is unused by this handler today — kept for signature parity with the rest of the
codebase's handlers and because Phase 2 will need it for `CorrectedAt`. If the compiler warns on
the unused field, that is expected and acceptable; do not remove the constructor parameter, as
`HandlerTestFixture`'s `FixedClock` convention assumes every handler takes one.)

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter "SubmitWritingEntryHandlerTests|WritingEntryTests"`
Expected: PASS (7 tests total across both files).

- [ ] **Step 8: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Writing backend/tests/MenuNest.Application.UnitTests/Writing/SubmitWritingEntryHandlerTests.cs
git commit -m "feat(writing): SubmitWritingEntry command + handler (#97)"
```

---

### Task 4: WebApi — `WritingEntriesController`

**Files:**
- Create: `backend/src/MenuNest.WebApi/Controllers/WritingEntriesController.cs`

**Interfaces:**
- Consumes: `SubmitWritingEntryCommand`, `WritingEntryDto` (Task 3).
- Produces: `POST /api/writing-entries` — the exact route the frontend (Task 5) calls.

- [ ] **Step 1: Write the controller**

```csharp
using Mediator;
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.SubmitWritingEntry;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
public sealed class WritingEntriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public WritingEntriesController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Submits tonight's 7-minute freewrite entry. Marks the day "done" —
    /// no correction happens here (see docs/decision-map/writing-practice-build).
    /// </summary>
    [HttpPost("api/writing-entries")]
    public async Task<ActionResult<WritingEntryDto>> Submit(
        [FromBody] SubmitWritingEntryCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}
```

- [ ] **Step 2: Build the backend to confirm it compiles**

Run: `dotnet build backend/MenuNest.sln` (or the solution's actual path/name — check for a `.sln`
file at `backend/` root first if this exact name is wrong)
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/WritingEntriesController.cs
git commit -m "feat(writing): expose POST /api/writing-entries (#97)"
```

---

### Task 5: Frontend — types + RTK Query endpoint

**Files:**
- Create: `frontend/src/shared/api/writingTypes.ts`
- Modify: `frontend/src/shared/api/api.ts`

**Interfaces:**
- Consumes: nothing new (RTK Query `createApi` builder already wired).
- Produces: `WritingEntryDto` type, `useSubmitWritingEntryMutation()` hook — consumed by
  `WritingPage` (Task 8).

- [ ] **Step 1: Write the types file**

```typescript
// frontend/src/shared/api/writingTypes.ts
export interface WritingEntryDto {
    id: string
    date: string // YYYY-MM-DD
    text: string
    elapsedSeconds: number
    wordsPerMinute: number
    createdAt: string
}

export interface SubmitWritingEntryRequest {
    date: string // YYYY-MM-DD
    text: string
    elapsedSeconds: number
}
```

- [ ] **Step 2: Add the endpoint to `api.ts`**

Add the import near the other feature-type imports at the top of `api.ts`:

```typescript
import type { WritingEntryDto, SubmitWritingEntryRequest } from './writingTypes'
```

Add `'WritingEntries'` to the `tagTypes` array (next to `'Trips'` etc.).

Add the endpoint (place it near the other feature sections, e.g. after Trips):

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
```

Add `useSubmitWritingEntryMutation` to the destructured export list at the bottom of `api.ts`
(find the existing `export const { ... } = api` block and add it there, matching the file's
existing style).

- [ ] **Step 3: Run the frontend typecheck**

Run: `cd frontend && npx tsc -b`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/shared/api/writingTypes.ts frontend/src/shared/api/api.ts
git commit -m "feat(writing): add writing-entries RTK Query endpoint (#97)"
```

---

### Task 6: Frontend — pure timer logic (`writingTimer.ts`) + vitest

**Files:**
- Create: `frontend/src/pages/writing/writingTimer.ts`
- Test: `frontend/src/pages/writing/writingTimer.test.ts`

**Interfaces:**
- Consumes: nothing (pure functions).
- Produces: `TIMER_DURATION_MS` (constant, 7 * 60 * 1000), `computeRemainingMs(startedAtMs: number,
  nowMs: number): number`, `isTimerDone(startedAtMs: number, nowMs: number): boolean` — consumed by
  `useWritingTimer` (Task 7).

This is the wall-clock computation `timer-resilience` requires (start timestamp + duration, no
pause), extracted as pure logic per `CLAUDE.md`'s "frontend has no component/visual test harness"
guidance so it gets real vitest coverage.

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/pages/writing/writingTimer.test.ts
import { describe, expect, it } from 'vitest'
import { TIMER_DURATION_MS, computeRemainingMs, isTimerDone } from './writingTimer'

describe('writingTimer', () => {
  it('TIMER_DURATION_MS is exactly 7 minutes', () => {
    expect(TIMER_DURATION_MS).toBe(7 * 60 * 1000)
  })

  it('computeRemainingMs counts down from the full duration at start', () => {
    const startedAt = 1_000_000
    expect(computeRemainingMs(startedAt, startedAt)).toBe(TIMER_DURATION_MS)
  })

  it('computeRemainingMs decreases as wall-clock time passes, regardless of ticks missed', () => {
    const startedAt = 1_000_000
    // Simulates a screen lock: no ticks fired, but 3 minutes of wall-clock
    // time passed before the next tick — the timer must reflect all of it.
    const threeMinutesLater = startedAt + 3 * 60 * 1000
    expect(computeRemainingMs(startedAt, threeMinutesLater)).toBe(4 * 60 * 1000)
  })

  it('computeRemainingMs never goes negative', () => {
    const startedAt = 1_000_000
    const wayLater = startedAt + TIMER_DURATION_MS + 60 * 60 * 1000
    expect(computeRemainingMs(startedAt, wayLater)).toBe(0)
  })

  it('isTimerDone is false before the duration elapses and true at/after it', () => {
    const startedAt = 1_000_000
    expect(isTimerDone(startedAt, startedAt)).toBe(false)
    expect(isTimerDone(startedAt, startedAt + TIMER_DURATION_MS - 1)).toBe(false)
    expect(isTimerDone(startedAt, startedAt + TIMER_DURATION_MS)).toBe(true)
    expect(isTimerDone(startedAt, startedAt + TIMER_DURATION_MS + 1)).toBe(true)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && npx vitest run src/pages/writing/writingTimer.test.ts`
Expected: FAIL — module `./writingTimer` does not exist.

- [ ] **Step 3: Write minimal implementation**

```typescript
// frontend/src/pages/writing/writingTimer.ts

/** The freewrite duration (daily-unit, habit-mechanics): 7 minutes, fixed. */
export const TIMER_DURATION_MS = 7 * 60 * 1000

/**
 * Wall-clock remaining time. Deliberately takes only a start timestamp and
 * "now" — no pause/resume state exists, per timer-resilience: the timer
 * keeps running through a screen lock or app switch, it never pauses.
 */
export function computeRemainingMs(startedAtMs: number, nowMs: number): number {
  const elapsed = nowMs - startedAtMs
  return Math.max(0, TIMER_DURATION_MS - elapsed)
}

/** True once the full 7 minutes have elapsed since startedAtMs. */
export function isTimerDone(startedAtMs: number, nowMs: number): boolean {
  return computeRemainingMs(startedAtMs, nowMs) <= 0
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && npx vitest run src/pages/writing/writingTimer.test.ts`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/writing/writingTimer.ts frontend/src/pages/writing/writingTimer.test.ts
git commit -m "feat(writing): add pure wall-clock timer logic (#97)"
```

---

### Task 7: Frontend — `useWritingTimer` hook

**Files:**
- Create: `frontend/src/pages/writing/useWritingTimer.ts`

**Interfaces:**
- Consumes: `TIMER_DURATION_MS`, `computeRemainingMs`, `isTimerDone` (Task 6).
- Produces: `useWritingTimer(): { remainingMs: number; isDone: boolean; startedAtMs: number }` —
  consumed by `WritingPage` (Task 8). The timer **starts automatically on mount** (no start button —
  the mockup's "เขียน" frame shows the countdown already running, matching the one-tap trigger:
  opening the page IS starting the session) and never exposes pause/resume, unlike
  `usePomodoroTimer`.

This is UI-wiring glue (a `useState`/`useEffect` interval reading the pure functions from Task 6),
not independently testable under this repo's node-environment vitest — it is exercised indirectly
via interactive verification in Task 9's final step, per `CLAUDE.md`'s "Any UI change MUST be
verified interactively" rule. Do not attempt to jsdom-test this hook.

- [ ] **Step 1: Write the hook**

```typescript
// frontend/src/pages/writing/useWritingTimer.ts
import { useEffect, useRef, useState } from 'react'
import { computeRemainingMs, isTimerDone } from './writingTimer'

export interface UseWritingTimer {
  remainingMs: number
  isDone: boolean
  startedAtMs: number
}

/**
 * Wall-clock 7-minute timer that starts the instant this hook mounts (the
 * writing page IS the trigger — one-tap-access) and never pauses
 * (timer-resilience): ticking is only ever a re-render, the underlying
 * remaining-time math is pure wall-clock arithmetic, so a screen lock or
 * app switch that stops ticks entirely still shows the correct time left
 * the moment ticking resumes.
 */
export function useWritingTimer(): UseWritingTimer {
  const startedAtRef = useRef<number>(Date.now())
  const [now, setNow] = useState<number>(() => Date.now())

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [])

  const startedAtMs = startedAtRef.current
  return {
    remainingMs: computeRemainingMs(startedAtMs, now),
    isDone: isTimerDone(startedAtMs, now),
    startedAtMs,
  }
}
```

- [ ] **Step 2: Run the frontend typecheck**

Run: `cd frontend && npx tsc -b`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/writing/useWritingTimer.ts
git commit -m "feat(writing): add useWritingTimer hook (#97)"
```

---

### Task 8: Frontend — `WritingPage` component (RTE + timer + submit)

**Files:**
- Create: `frontend/src/pages/writing/WritingPage.tsx`
- Create: `frontend/src/pages/writing/WritingPage.css`
- Create: `frontend/src/pages/writing/index.ts`
- Modify: `frontend/package.json`

**Interfaces:**
- Consumes: `useWritingTimer` (Task 7), `useSubmitWritingEntryMutation` (Task 5),
  `RichTextEditorComponent` + `Inject` + `Toolbar` + `Link` + `HtmlEditor` + `QuickToolbar` from
  `@syncfusion/ej2-react-richtexteditor`.
- Produces: `WritingPage` component — wired into the router in Task 9.

- [ ] **Step 1: Add the Syncfusion RTE dependency**

In `frontend/package.json`, add to `dependencies` (alphabetical position, matching the file's
existing `@syncfusion/*` block, pinned to match the repo's other `ej2-*` packages exactly):

```json
    "@syncfusion/ej2-react-richtexteditor": "^33.1.49",
```

Run: `cd frontend && npm install`
Expected: lockfile updates, install succeeds with no peer-dependency errors.

- [ ] **Step 2: Write the page component**

```tsx
// frontend/src/pages/writing/WritingPage.tsx
import { useRef, useState } from 'react'
import {
  RichTextEditorComponent,
  Inject,
  Toolbar,
  Link,
  HtmlEditor,
  QuickToolbar,
  type RichTextEditorComponent as RteInstance,
} from '@syncfusion/ej2-react-richtexteditor'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { useWritingTimer } from './useWritingTimer'
import { useSubmitWritingEntryMutation } from '../../shared/api/api'
import './WritingPage.css'

const formatMMSS = (ms: number): string => {
  const totalSec = Math.ceil(ms / 1000)
  const m = Math.floor(totalSec / 60)
  const s = totalSec % 60
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

const todayKey = (): string => {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

export function WritingPage() {
  const { remainingMs, isDone, startedAtMs } = useWritingTimer()
  const [submitWritingEntry, { isLoading, isSuccess }] = useSubmitWritingEntryMutation()
  const rteRef = useRef<RteInstance | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const handleSubmit = async () => {
    const html = rteRef.current?.getHtml?.() ?? ''
    const elapsedSeconds = Math.round((Date.now() - startedAtMs) / 1000)
    setSubmitError(null)
    try {
      await submitWritingEntry({
        date: todayKey(),
        text: html,
        elapsedSeconds,
      }).unwrap()
    } catch {
      setSubmitError('ส่งไม่สำเร็จ ลองอีกครั้ง')
    }
  }

  return (
    <div className="writing-page" data-testid="writing-page">
      <div className="writing-timer" data-testid="writing-timer">
        {formatMMSS(remainingMs)}
      </div>
      <div className="writing-timer-note">นับถอยหลังจาก 7:00 · เดินต่อแม้ล็อกหน้าจอ</div>

      <RichTextEditorComponent
        ref={rteRef}
        height={300}
        placeholder="เขียนถึงครอบครัววันนี้เป็นภาษาอังกฤษ..."
      >
        <Inject services={[Toolbar, Link, HtmlEditor, QuickToolbar]} />
      </RichTextEditorComponent>

      {isSuccess ? (
        <div className="writing-done-badge" data-testid="writing-done-badge">
          ✓ วันนี้เสร็จแล้ว
        </div>
      ) : (
        <Button
          variant={Variant.Standard}
          color={Color.Primary}
          onClick={handleSubmit}
          disabled={isLoading}
          data-testid="writing-submit"
        >
          {isDone ? 'ส่ง' : 'ส่งก่อนครบเวลา'}
        </Button>
      )}
      {submitError && <div className="writing-error">{submitError}</div>}
      <div className="writing-correction-note">แก้ทีหลังได้ ผ่าน Claude Code เมื่อไหร่ก็ได้</div>
    </div>
  )
}
```

Note: the exact `RichTextEditorComponent` ref API (`getHtml()` vs. a `value` prop / `change`
event) must be confirmed against the installed `@syncfusion/ej2-react-richtexteditor@33.1.49`
type definitions once Step 1's `npm install` completes — read
`frontend/node_modules/@syncfusion/ej2-react-richtexteditor/src/rich-text-editor/rich-text-editor.component.d.ts`
(or the package's own README) before finalizing this step, and adjust the ref call / import list
to match the real API rather than trusting the sketch above verbatim.

- [ ] **Step 3: Write the CSS**

```css
/* frontend/src/pages/writing/WritingPage.css */
.writing-page {
  max-width: 480px;
  margin: 0 auto;
  padding: 16px;
}

.writing-timer {
  text-align: center;
  font-size: 40px;
  font-weight: 700;
  color: var(--color-primary);
  letter-spacing: 1px;
  margin-bottom: 4px;
}

.writing-timer-note {
  text-align: center;
  color: var(--color-text-muted);
  font-size: 11px;
  margin-bottom: 14px;
}

.writing-done-badge {
  margin-top: 12px;
  background: #fff3e0;
  color: var(--color-primary);
  border-radius: 8px;
  padding: 8px 10px;
  font-size: 12px;
  text-align: center;
  font-weight: 600;
}

.writing-error {
  margin-top: 8px;
  color: var(--color-danger);
  font-size: 12px;
  text-align: center;
}

.writing-correction-note {
  margin-top: 8px;
  font-size: 11px;
  color: var(--color-text-muted);
  text-align: center;
}
```

- [ ] **Step 4: Write the barrel export**

```typescript
// frontend/src/pages/writing/index.ts
export { WritingPage } from './WritingPage'
```

- [ ] **Step 5: Run the frontend typecheck and build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: 0 errors, build succeeds.

- [ ] **Step 6: Commit**

```bash
git add frontend/package.json frontend/package-lock.json frontend/src/pages/writing/WritingPage.tsx frontend/src/pages/writing/WritingPage.css frontend/src/pages/writing/index.ts
git commit -m "feat(writing): add WritingPage with Syncfusion RTE + timer (#97)"
```

---

### Task 9: Wire into router, nav, and Home-page options

**Files:**
- Modify: `frontend/src/router.tsx`
- Modify: `frontend/src/shared/components/NavBar.tsx`
- Modify: `frontend/src/pages/settings/homeOptions.ts`

**Interfaces:**
- Consumes: `WritingPage` (Task 8).
- Produces: route `/writing` reachable from the nav and selectable as Home, matching
  `one-tap-access`'s "normal page in the existing nav" resolution.

- [ ] **Step 1: Add the route**

In `router.tsx`, add the import next to `PomodoroPage`:

```typescript
import { WritingPage } from './pages/writing'
```

Add the route inside the same `AppLayout` children array `/pomodoro` sits in (health-pages block —
no Family required, matches this feature's personal-scope nature):

```typescript
          { path: '/writing', element: <WritingPage /> },
```

- [ ] **Step 2: Add the nav entry**

In `NavBar.tsx`'s `navItems` array, add an entry. Match the plain-text-label style already used
by `'ไปไหนดี'` / `'Recipes'` (no emoji — `docs/frontend-guidelines.md`'s no-emoji-as-iconography
house style; the existing `🤒`/`⏱️`/`🧳` entries are legacy, not a pattern to extend):

```typescript
  { to: '/writing', label: 'เขียน' },
```

- [ ] **Step 3: Add it as a selectable Home option**

In `homeOptions.ts`'s `HOME_OPTIONS` array:

```typescript
  { path: '/writing', label: 'เขียน', requiresFamily: false },
```

- [ ] **Step 4: Run the frontend typecheck and build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: 0 errors, build succeeds.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/router.tsx frontend/src/shared/components/NavBar.tsx frontend/src/pages/settings/homeOptions.ts
git commit -m "feat(writing): wire the write page into nav, routing, Home options (#97)"
```

- [ ] **Step 6: Interactive verification (required — CLAUDE.md, no jsdom in this repo)**

Run the app (`npm run dev` in `frontend`, backend running locally), sign in, open `/writing` from
the nav:

1. Confirm the timer starts counting down from 07:00 the instant the page opens (no start button).
2. Lock the screen or switch away from the tab/app for ~30 seconds, then return — confirm the
   displayed time reflects the full elapsed wall-clock time, not a paused value (`timer-resilience`).
3. Type into the Rich Text Editor field — confirm the toolbar (bold/italic/underline/lists) is
   present and functional.
4. Submit before the timer reaches 00:00 — confirm it is allowed (the plan does not gate submit on
   `isDone`; `done-day-redefinition` only defines what counts as "done" for the habit, not a hard
   UI lock) and the "ส่งไม่สำเร็จ" error path does **not** fire on a normal submit.
5. Confirm the done badge replaces the submit button after a successful submit.
6. Confirm `/writing` appears in `/settings`'s Home-page picker and, once selected, `/` redirects
   there.

Fix anything found before considering this plan complete — do not defer interactive verification.

---

### Task 10: Apply the migration to prod, close the loop on the map

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

Confirm the `WritingEntries` table now exists on prod (e.g. `SELECT TOP 1 * FROM WritingEntries`
via the same connection, or check `dotnet ef migrations list` reports `AddWritingEntries` as
applied).

- [ ] **Step 2: Smoke-test on prod**

After the next deploy (push to `main` per `CLAUDE.md`), repeat Task 9 Step 6's interactive check
against the deployed app, not just localhost.

- [ ] **Step 3: Update the decision map / project memory**

No ticket on `docs/decision-map/writing-practice-build/` tracks "the write page is built" as its
own ticket (Phase 1 needed zero more decisions — that was the point). Once verified on prod, tell
the user night 1's date can now be recorded, and that `pending-correction-visibility` remains the
one open ticket for Phase 2.

---

## Self-Review

**Spec coverage:** `timer-resilience` (Task 6/7 — pure wall-clock math, no pause) OK;
`done-day-redefinition` (Task 8 — submit is not gated on `isDone`, matching "the 7-minute timer
alone counts as done" being about the *habit*, not a UI lock — the writer can still submit early
or late) OK; `one-tap-access` (Task 9 — plain nav entry, no PWA shortcut) OK; RTE choice (Task 8,
`@syncfusion/ej2-react-richtexteditor@33.1.49`) OK; schema reserved for Phase 2 without a second
migration (Task 1/2) OK; three-DbContext rule (Task 2 Step 1) OK; issue #97 referenced in every
commit OK; interactive verification required before done (Task 9 Step 6, Task 10 Step 2) OK.

**Placeholder scan:** no TBD/TODO; the two explicitly-flagged "confirm against the real
API/convention before finalizing" notes (Task 2 Step 3's `SqliteAppDbContext` factory name, Task 8
Step 2's RTE ref API) are deliberate — they name a concrete file to check, not an unresolved gap,
because the exact API surface cannot be verified from outside the installed package/test-support
file at plan-writing time.

**Type consistency:** `WritingEntryDto` field names/types match end-to-end: Domain entity
properties (Task 1) -> DTO (Task 3 Step 1) -> controller return type (Task 4) -> frontend
`WritingEntryDto` (Task 5 Step 1). `SubmitWritingEntryCommand`'s three fields match
`SubmitWritingEntryRequest`'s three fields (Task 5 Step 1) exactly, both in name and shape
(`Date`/`date`, `Text`/`text`, `ElapsedSeconds`/`elapsedSeconds`).
