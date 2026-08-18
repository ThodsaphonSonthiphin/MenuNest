# Writing-practice ผลตรวจ Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use sp-subagent-driven-development (recommended) or sp-executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the AI correction that Phase 2 already stores — the five fixed blocks of one night's ผลตรวจ — on the corrected entry's own page, fed by a new by-id endpoint and guarded by a real HTML sanitizer.

**Architecture:** `/writing/history/:id` becomes one route with two states switched on `correctedAt` (ADR-177). A new `GET /api/writing-entries/{id}` carries the entry plus its correction and the one derived number the screen needs (`errorsPer100Words`), computed server-side with the same word counter that already produces `wordsPerMinute` (ADR-179). The list endpoint is untouched, because it is polled and `MarkedText` is bounded at 50,000 characters. `MarkedText` is model-authored HTML, so it passes through DOMPurify behind a closed allow-list before it reaches the DOM (ADR-180).

**Tech Stack:** .NET 9 (net10.0 test TFM), Mediator (source-generated), EF Core (SQL Server prod / InMemory tests), xUnit + Moq + FluentAssertions, React 19 + RTK Query + Syncfusion, DOMPurify 3.4.13, vitest 4 + jsdom, Playwright.

**Spec:** this plan implements four ADRs and a mock; executors read all of them:
- `docs/adr/177-a-corrected-nights-own-page-becomes-the-result-screen.md` — one route, two states
- `docs/adr/178-the-result-screen-renders-the-correction-as-it-actually-arrives.md` — **the render rules; read this before Task 5**
- `docs/adr/179-a-correction-is-fetched-by-id-and-its-numbers-are-derived-server-side.md` — the API shape
- `docs/adr/180-marked-text-is-sanitized-with-dompurify-behind-a-closed-allow-list.md` — the sanitizer contract
- `CONTEXT.md` — the writing glossary (**Correction**, **Marked text**, **Thai why-line**, **Stuck word**, **Words-per-minute**)
- Approved mock: Claude Design project `MenuNest design system` (id `8d8d4c81-41c1-4e0a-a0b7-370b39dfbe70`), card **`screens/issue-97-correction-result.html`** — frame 1 is the target screen, frame 2 is the same screen with a fully-populated night

---

## Global Constraints

- **No migration in this plan.** Every column it reads already exists in prod and is already stamped in `__EFMigrationsHistory` (`AddUserSettingsActiveTargetRule`, `AddWritingEntryMarkedText`, both verified 2026-08-18). Do **not** add one; if a step seems to need one, stop and ask.
- **Every commit must leave the WHOLE suite green.** `frontend/.husky/pre-commit` runs backend `dotnet build` + `dotnet test` (Release) and frontend `tsc --noEmit` + `npm run build` on every commit (~40s+). Never `--no-verify`.
- **`git add <explicit paths>` only** — never `git add -A` / `git add .`. `daily-state.md` (tracked, usually dirty) and `AGENTS.md` (untracked) must never enter a feature commit.
- **Every commit references the ticket:** `type(scope): summary (#97)`. Use `(closes #97)` only if the writer confirms the whole feature is done.
- **The five blocks are a closed, ordered set.** Block order is 1 Marked text → 2 Thai why-line → 3 Sentence combining → 4 Stuck words → 5 Numbers. All five render on every corrected night; an empty one states why (ADR-178). Do not add a sixth block, and do not reorder.
- **A **Correction** returns evidence, never judgement.** No score, no praise, no streak, no rewrite of the writer's text — the "สิ่งที่ระบบจะไม่ทำเด็ดขาด" note ships in the UI and says so.
- **Thai strings are UI copy and must be copied verbatim** from this plan. They are matched by Playwright specs.
- **Vitest runs `environment: 'node'`** (`frontend/vite.config.ts`) and `include: ['src/**/*.test.ts']` — `.test.tsx` files are **not** collected. Task 3 adds jsdom for one file via a docblock; it does not change the global environment.
- **Backend `dotnet test` counts as of this plan's start:** 786 passing. Frontend unit: 394 passing. Do not let either drop.
- **Sanitizer allow-list (exact):** tags `p`, `span`, `br`; attribute `class`; class values `miss`, `fix`, `hit`, `th`. Changing any of these is a security change, not styling.
- **`errorsPer100Words` rounding:** one decimal place, `MidpointRounding.AwayFromZero`.
- **`entry.text` is NOT sanitized** and its existing `dangerouslySetInnerHTML` render stays exactly as it is — it is the writer's own Syncfusion-RTE output and legitimately contains `<b>`, `<ul>`, etc.

---

## File Structure

**Backend — create:**

| Path | Responsibility |
|---|---|
| `backend/src/MenuNest.Application/UseCases/Writing/GetWritingEntry/GetWritingEntryQuery.cs` | Query carrying the entry id |
| `backend/src/MenuNest.Application/UseCases/Writing/GetWritingEntry/GetWritingEntryHandler.cs` | Loads the user-scoped entry, deserialises the two JSON columns, derives `errorsPer100Words` |

**Backend — modify:**

| Path | Change |
|---|---|
| `backend/src/MenuNest.Domain/Entities/WritingEntry.cs` | `private static int CountWords` → `public static int CountWords` (no other change) |
| `backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs` | `+ WritingCorrectionDto`, `+ WritingEntryDetailDto` |
| `backend/src/MenuNest.WebApi/Controllers/WritingEntriesController.cs` | `+ GET /api/writing-entries/{id:guid}` |

**Backend — test (create):** `backend/tests/MenuNest.Application.UnitTests/Writing/GetWritingEntryHandlerTests.cs`, `backend/tests/MenuNest.WebApi.UnitTests/Controllers/WritingEntriesControllerGetByIdTests.cs`

**Frontend — create:**

| Path | Responsibility |
|---|---|
| `frontend/src/pages/writing/sanitizeMarkedText.ts` | The security boundary: DOMPurify + closed allow-list + class filter. One export. |
| `frontend/src/pages/writing/sanitizeMarkedText.test.ts` | Attack strings, under jsdom |
| `frontend/src/pages/writing/CorrectionResult.tsx` | The five blocks. Presentational — takes a `WritingCorrectionDto` and the entry's numbers, renders, owns no data fetching. |
| `frontend/src/pages/writing/CorrectionResult.css` | Block styling, ported from the mock card |
| `frontend/e2e/writing.correction-result.spec.ts` | Rendering smoke for the corrected state |

**Frontend — modify:** `frontend/src/shared/api/writingTypes.ts` (correction types), `frontend/src/shared/api/api.ts` (`getWritingEntry`), `frontend/src/pages/writing/WritingEntryDetailPage.tsx` (state switch + new data source), `frontend/src/pages/writing/WritingEntryDetailPage.css` (locked-note removal), `frontend/e2e/writing.live-lock.spec.ts` (route + assertions), `frontend/package.json` (`dompurify`, `jsdom`).

---

## Task 1: `GetWritingEntry` use case — entry + correction + the derived number

Implements ADR-179's read side. Nothing renders yet.

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/WritingEntry.cs:148`
- Modify: `backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Writing/GetWritingEntry/GetWritingEntryQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Writing/GetWritingEntry/GetWritingEntryHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Writing/GetWritingEntryHandlerTests.cs`

**Interfaces:**
- Consumes: `IApplicationDbContext.WritingEntries`, `IUserProvisioner.GetOrProvisionCurrentAsync` (both already exist and are used by `ListWritingEntriesHandler`); the existing `SentenceCombiningItemDto(string Source, string Combined)` and `StuckWordDto(string Thai, string English)`.
- Produces:
  - `GetWritingEntryQuery(Guid Id) : IQuery<WritingEntryDetailDto>` — Task 2 sends this.
  - `WritingEntryDetailDto(Guid Id, DateOnly Date, string Text, int ElapsedSeconds, double WordsPerMinute, DateTime? CorrectedAt, DateTime CreatedAt, WritingCorrectionDto? Correction)` — Task 2 returns it; Task 4 mirrors it in TypeScript.
  - `WritingCorrectionDto(string TargetRule, string MarkedText, int HitCount, int MissCount, string ThaiWhyLine, IReadOnlyList<SentenceCombiningItemDto> SentenceCombiningItems, IReadOnlyList<StuckWordDto> StuckWords, double ErrorsPer100Words)`
  - `WritingEntry.CountWords(string html)` becomes **public static** — the handler calls it.

**Critical detail — the JSON columns are PascalCase.** `RecordWritingCorrectionHandler` serialises the C# records with default naming, so the stored text is `[{"Thai":"…","English":"…"}]` (verified against the live prod row on 2026-08-18). Deserialise with default options — the property names already match the record. Do **not** add `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`; it would silently produce empty strings.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Writing/GetWritingEntryHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.GetWritingEntry;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Writing;

public class GetWritingEntryHandlerTests
{
    private const string SevenWordText = "<p>one two three four five six seven</p>";

    private static WritingEntry SeedCorrected(
        HandlerTestFixture fx,
        string text = SevenWordText,
        int hitCount = 2,
        int missCount = 3,
        string sentenceCombiningJson = "[]",
        string stuckWordsJson = "[]")
    {
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), text, 420);
        entry.RecordCorrection(
            correctedAtUtc: new DateTime(2026, 8, 17, 14, 57, 23, DateTimeKind.Utc),
            targetRule: "articles (a/an/the)",
            markedText: "<p><span class=\"hit\">one</span> two</p>",
            hitCount: hitCount,
            missCount: missCount,
            thaiWhyLine: "คำนามนับได้เอกพจน์ต้องมีตัวนำหน้าเสมอ",
            sentenceCombiningItemsJson: sentenceCombiningJson,
            stuckWordsJson: stuckWordsJson);
        fx.Db.WritingEntries.Add(entry);
        return entry;
    }

    [Fact]
    public async Task Returns_a_null_correction_for_a_night_that_was_never_corrected()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), SevenWordText, 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Id.Should().Be(entry.Id);
        result.Text.Should().Be(SevenWordText);
        result.CorrectedAt.Should().BeNull();
        result.Correction.Should().BeNull("a pending night has no correction to carry");
    }

    [Fact]
    public async Task Carries_all_five_blocks_of_a_recorded_correction()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(
            fx,
            sentenceCombiningJson: "[{\"Source\":\"Traffic is bad. + We arrive late.\",\"Combined\":\"Traffic was bad, so we arrived late.\"}]",
            stuckWordsJson: "[{\"Thai\":\"ข้าวต้ม\",\"English\":\"rice porridge / congee\"}]");
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Correction.Should().NotBeNull();
        var c = result.Correction!;
        c.TargetRule.Should().Be("articles (a/an/the)");
        c.MarkedText.Should().Be("<p><span class=\"hit\">one</span> two</p>");
        c.HitCount.Should().Be(2);
        c.MissCount.Should().Be(3);
        c.ThaiWhyLine.Should().Be("คำนามนับได้เอกพจน์ต้องมีตัวนำหน้าเสมอ");
        c.SentenceCombiningItems.Should().HaveCount(1);
        c.SentenceCombiningItems[0].Source.Should().Be("Traffic is bad. + We arrive late.");
        c.SentenceCombiningItems[0].Combined.Should().Be("Traffic was bad, so we arrived late.");
        c.StuckWords.Should().HaveCount(1);
        c.StuckWords[0].Thai.Should().Be("ข้าวต้ม");
        c.StuckWords[0].English.Should().Be("rice porridge / congee");
    }

    [Fact]
    public async Task Deserialises_the_pascal_case_json_the_recorder_actually_writes()
    {
        // RecordWritingCorrectionHandler serialises the C# records with default
        // naming, so the stored text is PascalCase. A camelCase policy here would
        // deserialise every field to an empty string without failing.
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(
            fx,
            stuckWordsJson: "[{\"Thai\":\"ซุซิสายพาน\",\"English\":\"conveyor-belt sushi\"}]");
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Correction!.StuckWords[0].Thai.Should().Be("ซุซิสายพาน");
        result.Correction!.StuckWords[0].English.Should().Be("conveyor-belt sushi");
    }

    [Fact]
    public async Task An_empty_json_array_becomes_an_empty_list_not_a_null()
    {
        // The only real production correction has SentenceCombiningItemsJson = "[]"
        // (a Thai-only night). The screen renders an empty block for it, so the
        // list must arrive empty rather than null.
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(fx, sentenceCombiningJson: "[]", stuckWordsJson: "[]");
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Correction!.SentenceCombiningItems.Should().BeEmpty();
        result.Correction!.StuckWords.Should().BeEmpty();
    }

    [Fact]
    public async Task Derives_errors_per_100_words_to_one_decimal_place()
    {
        // 3 misses over 7 words = 42.857... -> 42.9
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(fx, text: SevenWordText, missCount: 3);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Correction!.ErrorsPer100Words.Should().Be(42.9);
    }

    [Fact]
    public async Task A_thai_only_night_with_no_misses_derives_zero()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(fx, text: "<p>[วันนี้พาลูกสาวไปกินข้าวเย็น]</p>", hitCount: 0, missCount: 0);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        result.Correction!.HitCount.Should().Be(0);
        result.Correction!.MissCount.Should().Be(0);
        result.Correction!.ErrorsPer100Words.Should().Be(0);
    }

    [Fact]
    public async Task Refuses_an_unknown_id_with_the_standard_message()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await handler.Handle(new GetWritingEntryQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Writing entry not found.");
    }

    [Fact]
    public async Task Refuses_a_soft_deleted_entry()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var entry = SeedCorrected(fx);
        entry.SoftDelete();
        await fx.Db.SaveChangesAsync();

        var act = async () => await handler.Handle(new GetWritingEntryQuery(entry.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Writing entry not found.");
    }

    [Fact]
    public async Task Refuses_another_users_entry_with_the_same_message()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetWritingEntryHandler(fx.Db, fx.UserProvisioner.Object);

        var otherUser = User.CreateFromExternalLogin(
            externalId: "other-oid",
            email: "other@example.com",
            displayName: "Other User",
            authProvider: AuthProvider.Microsoft);
        fx.Db.Users.Add(otherUser);
        var othersEntry = WritingEntry.Create(otherUser.Id, new DateOnly(2026, 8, 16), SevenWordText, 420);
        fx.Db.WritingEntries.Add(othersEntry);
        await fx.Db.SaveChangesAsync();

        var act = async () => await handler.Handle(new GetWritingEntryQuery(othersEntry.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Writing entry not found.");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~GetWritingEntryHandlerTests`
Expected: **build failure** — `GetWritingEntryHandler`, `GetWritingEntryQuery`, `WritingEntryDetailDto` and `WritingCorrectionDto` do not exist.

- [ ] **Step 3: Make `CountWords` public**

In `backend/src/MenuNest.Domain/Entities/WritingEntry.cs`, change the one modifier and extend the existing doc comment:

```csharp
    /// <summary>
    /// Approximate word count of RTE-produced HTML: strips tags, normalizes
    /// HTML whitespace entities (&nbsp;) to real spaces, collapses
    /// whitespace, splits on spaces. Good enough for a words-per-minute
    /// signal — not a precise linguistic tokenizer.
    ///
    /// Public because errors-per-100-words is derived from the same count as
    /// WordsPerMinute (ADR-179): a second tokenizer, anywhere, would let the
    /// two numbers on one screen be computed from different word counts.
    /// </summary>
    public static int CountWords(string html)
```

- [ ] **Step 4: Add the DTOs**

Append to `backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs`:

```csharp
/// <summary>
/// One recorded Correction, as the ผลตรวจ screen needs it: the five blocks of
/// mcp-tool-contract plus the one number derived on the way out (ADR-179).
/// The two JSON columns arrive here already deserialised.
/// </summary>
public sealed record WritingCorrectionDto(
    string TargetRule,
    string MarkedText,
    int HitCount,
    int MissCount,
    string ThaiWhyLine,
    IReadOnlyList<SentenceCombiningItemDto> SentenceCombiningItems,
    IReadOnlyList<StuckWordDto> StuckWords,
    double ErrorsPer100Words);

/// <summary>
/// One writing entry with its Correction, as returned by
/// GET /api/writing-entries/{id}. Correction is null while the night is
/// pending. Deliberately NOT the shape of the list endpoint: MarkedText is
/// bounded at 50,000 characters and the History grid needs none of it.
/// </summary>
public sealed record WritingEntryDetailDto(
    Guid Id,
    DateOnly Date,
    string Text,
    int ElapsedSeconds,
    double WordsPerMinute,
    DateTime? CorrectedAt,
    DateTime CreatedAt,
    WritingCorrectionDto? Correction);
```

- [ ] **Step 5: Add the query**

Create `backend/src/MenuNest.Application/UseCases/Writing/GetWritingEntry/GetWritingEntryQuery.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Writing.GetWritingEntry;

/// <summary>
/// Reads one writing entry with its Correction for the ผลตรวจ screen
/// (ADR-177/ADR-179). Scoped to the calling user; a missing, deleted or
/// foreign id all answer with the same "not found" message.
/// </summary>
public sealed record GetWritingEntryQuery(Guid Id) : IQuery<WritingEntryDetailDto>;
```

- [ ] **Step 6: Add the handler**

Create `backend/src/MenuNest.Application/UseCases/Writing/GetWritingEntry/GetWritingEntryHandler.cs`:

```csharp
using System.Text.Json;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.GetWritingEntry;

public sealed class GetWritingEntryHandler
    : IQueryHandler<GetWritingEntryQuery, WritingEntryDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetWritingEntryHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<WritingEntryDetailDto> Handle(
        GetWritingEntryQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        // Same guard and same message as every other writing handler — "not
        // found" for a missing, deleted, or foreign entry alike.
        var entry = await _db.WritingEntries
            .FirstOrDefaultAsync(w => w.Id == query.Id && w.UserId == user.Id && w.DeletedAt == null, ct)
            ?? throw new DomainException("Writing entry not found.");

        return new WritingEntryDetailDto(
            Id: entry.Id,
            Date: entry.Date,
            Text: entry.Text,
            ElapsedSeconds: entry.ElapsedSeconds,
            WordsPerMinute: entry.WordsPerMinute,
            CorrectedAt: entry.CorrectedAt,
            CreatedAt: entry.CreatedAt,
            Correction: BuildCorrection(entry));
    }

    private static WritingCorrectionDto? BuildCorrection(WritingEntry entry)
    {
        if (entry.CorrectedAt is null) return null;

        var missCount = entry.MissCount ?? 0;
        var wordCount = WritingEntry.CountWords(entry.Text);
        var errorsPer100Words = wordCount == 0
            ? 0d
            : Math.Round(missCount * 100d / wordCount, 1, MidpointRounding.AwayFromZero);

        return new WritingCorrectionDto(
            TargetRule: entry.TargetRule ?? string.Empty,
            MarkedText: entry.MarkedText ?? string.Empty,
            HitCount: entry.HitCount ?? 0,
            MissCount: missCount,
            ThaiWhyLine: entry.ThaiWhyLine ?? string.Empty,
            SentenceCombiningItems: DeserialiseList<SentenceCombiningItemDto>(entry.SentenceCombiningItemsJson),
            StuckWords: DeserialiseList<StuckWordDto>(entry.StuckWordsJson),
            ErrorsPer100Words: errorsPer100Words);
    }

    /// <summary>
    /// Default JsonSerializerOptions on purpose: RecordWritingCorrectionHandler
    /// writes the records with default (PascalCase) naming, so the stored text
    /// is {"Thai":…}. A camelCase policy here would deserialise every field to
    /// an empty string silently.
    /// </summary>
    private static IReadOnlyList<T> DeserialiseList<T>(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Array.Empty<T>()
            : JsonSerializer.Deserialize<List<T>>(json) ?? [];
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~GetWritingEntryHandlerTests`
Expected: PASS, 9 tests.

- [ ] **Step 8: Run the whole backend suite**

Run: `cd backend && dotnet test`
Expected: PASS, no fewer than 786 + 9 tests.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/WritingEntry.cs \
        backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs \
        backend/src/MenuNest.Application/UseCases/Writing/GetWritingEntry/ \
        backend/tests/MenuNest.Application.UnitTests/Writing/GetWritingEntryHandlerTests.cs
git commit -m "feat(writing): read one entry with its correction and derived error rate (#97)"
```

---

## Task 2: `GET /api/writing-entries/{id}`

The wire boundary for Task 1. Implements ADR-179's endpoint.

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Controllers/WritingEntriesController.cs`
- Test: `backend/tests/MenuNest.WebApi.UnitTests/Controllers/WritingEntriesControllerGetByIdTests.cs`

**Interfaces:**
- Consumes: `GetWritingEntryQuery`, `WritingEntryDetailDto` (Task 1).
- Produces: `GET /api/writing-entries/{id:guid}` returning `WritingEntryDetailDto` as JSON — Task 4's RTK Query endpoint calls it.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/MenuNest.WebApi.UnitTests/Controllers/WritingEntriesControllerGetByIdTests.cs`:

```csharp
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.GetWritingEntry;
using MenuNest.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Controllers;

/// <summary>
/// Wire-boundary test for GET /api/writing-entries/{id}. The action must bind
/// the route id into GetWritingEntryQuery and return the detail DTO unchanged;
/// a test that sends the query directly proves nothing about the controller.
/// </summary>
public sealed class WritingEntriesControllerGetByIdTests
{
    [Fact]
    public async Task GetById_sends_the_route_id_as_the_query_and_returns_the_detail_dto()
    {
        var mediator = new Mock<IMediator>();
        GetWritingEntryQuery? captured = null;
        var id = Guid.NewGuid();

        var expected = new WritingEntryDetailDto(
            Id: id,
            Date: new DateOnly(2026, 8, 16),
            Text: "<p>one two three</p>",
            ElapsedSeconds: 420,
            WordsPerMinute: 0.43,
            CorrectedAt: new DateTime(2026, 8, 17, 14, 57, 23, DateTimeKind.Utc),
            CreatedAt: new DateTime(2026, 8, 16, 15, 0, 0, DateTimeKind.Utc),
            Correction: new WritingCorrectionDto(
                TargetRule: "articles (a/an/the)",
                MarkedText: "<p><span class=\"hit\">one</span> two three</p>",
                HitCount: 1,
                MissCount: 0,
                ThaiWhyLine: "คำนามนับได้เอกพจน์ต้องมีตัวนำหน้าเสมอ",
                SentenceCombiningItems: [],
                StuckWords: [],
                ErrorsPer100Words: 0));

        mediator
            .Setup(m => m.Send(It.IsAny<GetWritingEntryQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IQuery<WritingEntryDetailDto>, CancellationToken>((q, _) => captured = (GetWritingEntryQuery)q)
            .Returns(new ValueTask<WritingEntryDetailDto>(expected));

        var controller = new WritingEntriesController(mediator.Object);

        var result = await controller.GetById(id, CancellationToken.None);

        captured.Should().NotBeNull("the controller must send exactly one GetWritingEntryQuery");
        captured!.Id.Should().Be(id, "the route id must reach the query unchanged");

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeSameAs(expected, "the controller must not reshape the DTO");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test tests/MenuNest.WebApi.UnitTests --filter FullyQualifiedName~WritingEntriesControllerGetByIdTests`
Expected: **build failure** — `WritingEntriesController.GetById` does not exist.

- [ ] **Step 3: Add the action**

In `backend/src/MenuNest.WebApi/Controllers/WritingEntriesController.cs`, add the `using` and insert the action **between** `List` and `UpdateText`:

```csharp
using MenuNest.Application.UseCases.Writing.GetWritingEntry;
```

```csharp
    /// <summary>
    /// Reads one entry with its correction -- feeds the "ผลตรวจ" screen
    /// (ADR-177). Deliberately separate from List: MarkedText is bounded at
    /// 50,000 characters and List is polled (ADR-179).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WritingEntryDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetWritingEntryQuery(id), ct);
        return Ok(result);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test tests/MenuNest.WebApi.UnitTests --filter FullyQualifiedName~WritingEntriesControllerGetByIdTests`
Expected: PASS, 1 test.

- [ ] **Step 5: Run the whole backend suite**

Run: `cd backend && dotnet test`
Expected: PASS. No route-conflict error at startup — `[HttpGet]` and `[HttpGet("{id:guid}")]` do not collide.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/WritingEntriesController.cs \
        backend/tests/MenuNest.WebApi.UnitTests/Controllers/WritingEntriesControllerGetByIdTests.cs
git commit -m "feat(writing): expose GET /api/writing-entries/{id} with the correction (#97)"
```

---

## Task 3: `sanitizeMarkedText` — the security boundary

Implements ADR-180. Adds the only two dependencies in this plan. Nothing renders yet.

**Files:**
- Modify: `frontend/package.json`
- Create: `frontend/src/pages/writing/sanitizeMarkedText.ts`
- Test: `frontend/src/pages/writing/sanitizeMarkedText.test.ts`

**Interfaces:**
- Produces: `sanitizeMarkedText(markedText: string): string` — Task 5's `CorrectionResult` is the only caller.

**Why `RETURN_DOM` and not a hook:** DOMPurify's `addHook` mutates global state that would leak between callers and tests. Sanitising to a DOM node, filtering `class` with the DOM API, then reading `innerHTML` keeps the function pure and needs no teardown.

- [ ] **Step 1: Install the dependencies**

```bash
cd frontend && npm install dompurify@3.4.13 && npm install --save-dev jsdom
```

Expected: `dompurify` lands in `dependencies`, `jsdom` in `devDependencies`. DOMPurify 3.x ships its own types — do **not** install `@types/dompurify` (it is a deprecated stub).

- [ ] **Step 2: Write the failing tests**

Create `frontend/src/pages/writing/sanitizeMarkedText.test.ts`:

```ts
// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { sanitizeMarkedText } from './sanitizeMarkedText'

describe('sanitizeMarkedText', () => {
  it('keeps a miss/fix pair exactly as the correction wrote it', () => {
    const input = '<p>She <span class="miss">go</span> <span class="fix">→ goes</span> home.</p>'
    expect(sanitizeMarkedText(input)).toBe(input)
  })

  it('keeps a hit span and a bracketed Thai span', () => {
    const input = '<p>Traffic <span class="hit">is</span> bad. <span class="th">[ข้าวต้ม]</span></p>'
    expect(sanitizeMarkedText(input)).toBe(input)
  })

  it('round-trips the real production marked text unchanged', () => {
    const input =
      '<p><span class="th">[วันนี้พาลูกสาวไปกินข้าวเย็น และกินซุซิสายพานกับภรรยา ที่ห้าง passione]</span></p>'
    expect(sanitizeMarkedText(input)).toBe(input)
  })

  it('removes a script tag and its contents', () => {
    const out = sanitizeMarkedText('<p>hello<script>alert(1)</script></p>')
    expect(out).toBe('<p>hello</p>')
    expect(out).not.toContain('alert')
  })

  it('removes an img with an onerror handler', () => {
    const out = sanitizeMarkedText('<p>hi <img src=x onerror="alert(1)"> there</p>')
    expect(out).not.toContain('<img')
    expect(out).not.toContain('onerror')
  })

  it('removes an anchor entirely, javascript: href and all', () => {
    const out = sanitizeMarkedText('<p><a href="javascript:alert(1)">tap</a></p>')
    expect(out).not.toContain('<a')
    expect(out).not.toContain('javascript:')
    expect(out).toContain('tap')
  })

  it('drops a class the app owns, keeping the element and its text', () => {
    const out = sanitizeMarkedText('<p><span class="writing-detail-delete-btn">go</span></p>')
    expect(out).toBe('<p><span>go</span></p>')
  })

  it('keeps only the allowed class when an unknown one is smuggled alongside', () => {
    const out = sanitizeMarkedText('<p><span class="miss evil">go</span></p>')
    expect(out).toBe('<p><span class="miss">go</span></p>')
  })

  it('strips a style attribute even on an allowed tag', () => {
    const out = sanitizeMarkedText('<p style="position:fixed;inset:0">hi</p>')
    expect(out).toBe('<p>hi</p>')
  })

  it('returns an empty string for an empty correction', () => {
    expect(sanitizeMarkedText('')).toBe('')
  })
})
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/pages/writing/sanitizeMarkedText.test.ts`
Expected: FAIL — `Failed to resolve import "./sanitizeMarkedText"`.

- [ ] **Step 4: Write the implementation**

Create `frontend/src/pages/writing/sanitizeMarkedText.ts`:

```ts
import DOMPurify from 'dompurify'

/**
 * The four marks a Correction is allowed to paint (ADR-180). Anything else in a
 * class attribute — including MenuNest's own class names — is stripped, so a
 * correction can never repaint the page with the app's styles.
 */
const ALLOWED_MARK_CLASSES = new Set(['miss', 'fix', 'hit', 'th'])

/**
 * Sanitises the AI-authored `markedText` of a Correction down to a closed
 * allow-list before it reaches the DOM.
 *
 * `markedText` is HTML written by a language model and stored verbatim — the
 * recording path does no sanitising by design — so this is the boundary. The
 * allow-list and the class filter together ARE the security contract: widening
 * either is a security change, not styling.
 */
export function sanitizeMarkedText(markedText: string): string {
  const root = DOMPurify.sanitize(markedText, {
    ALLOWED_TAGS: ['p', 'span', 'br'],
    ALLOWED_ATTR: ['class'],
    RETURN_DOM: true,
  }) as HTMLElement

  for (const element of Array.from(root.querySelectorAll('[class]'))) {
    const kept = Array.from(element.classList).filter((c) => ALLOWED_MARK_CLASSES.has(c))
    if (kept.length === 0) element.removeAttribute('class')
    else element.setAttribute('class', kept.join(' '))
  }

  return root.innerHTML
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/pages/writing/sanitizeMarkedText.test.ts`
Expected: PASS, 10 tests.

- [ ] **Step 6: Run the whole frontend unit suite and the type check**

Run: `cd frontend && npm test && npx tsc --noEmit`
Expected: PASS, no fewer than 394 + 10 tests; zero type errors.

- [ ] **Step 7: Commit**

```bash
git add frontend/package.json frontend/package-lock.json \
        frontend/src/pages/writing/sanitizeMarkedText.ts \
        frontend/src/pages/writing/sanitizeMarkedText.test.ts
git commit -m "feat(writing): sanitize marked text with DOMPurify behind a closed allow-list (#97)"
```

---

## Task 4: The by-id query in the SPA

Types and data plumbing only — no rendering change yet, so the page keeps working exactly as it does today.

**Files:**
- Modify: `frontend/src/shared/api/writingTypes.ts`
- Modify: `frontend/src/shared/api/api.ts:1633` (immediately after `listWritingEntries`)

**Interfaces:**
- Consumes: `GET /api/writing-entries/{id}` (Task 2).
- Produces:
  - Types `SentenceCombiningItem`, `StuckWord`, `WritingCorrectionDto`, `WritingEntryDetailDto`
  - Hook `useGetWritingEntryQuery(id: string, options?)` — Task 5 is the only caller.

- [ ] **Step 1: Add the types**

Append to `frontend/src/shared/api/writingTypes.ts`:

```ts
export interface SentenceCombiningItem {
    source: string
    combined: string
}

export interface StuckWord {
    thai: string
    english: string
}

/** One recorded Correction: the five fixed blocks plus the derived error rate. */
export interface WritingCorrectionDto {
    targetRule: string
    markedText: string
    hitCount: number
    missCount: number
    thaiWhyLine: string
    sentenceCombiningItems: SentenceCombiningItem[]
    stuckWords: StuckWord[]
    errorsPer100Words: number
}

/** One entry with its Correction. `correction` is null while the night is pending. */
export interface WritingEntryDetailDto extends WritingEntryDto {
    correction: WritingCorrectionDto | null
}
```

- [ ] **Step 2: Add the endpoint**

In `frontend/src/shared/api/api.ts`, extend the type import on line 42 and add the query directly after `listWritingEntries`:

```ts
import type {SubmitWritingEntryRequest, UpdateWritingEntryTextRequest, WritingEntryDto, WritingEntryDetailDto} from './writingTypes'
```

```ts
        // The detail page reads THIS, not the list: markedText is bounded at
        // 50,000 characters and the list is polled (ADR-179).
        getWritingEntry: build.query<WritingEntryDetailDto, string>({
            query: (id) => `/api/writing-entries/${id}`,
            providesTags: (_r, _e, id) => [{type: 'WritingEntries', id}],
        }),
```

`updateWritingEntryText` and `deleteWritingEntry` already invalidate `{type: 'WritingEntries', id}`, so a text edit refetches this automatically — no change needed there.

- [ ] **Step 3: Verify it type-checks and builds**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: zero errors. (No test in this step: the endpoint has no behaviour of its own, and RTK Query wiring is proven by Task 6's e2e.)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/shared/api/writingTypes.ts frontend/src/shared/api/api.ts
git commit -m "feat(writing): add the by-id writing entry query with correction types (#97)"
```

---

## Task 5: The ผลตรวจ screen

The visible deliverable. Implements ADR-177 (one route, two states) and ADR-178 (all five blocks, full-length why-line, two-line stuck-word cards).

**Read `docs/adr/178-*.md` before writing this task's code.** Every rule below comes from it, and the empty-state copy is load-bearing — Task 6's specs match on it.

**Files:**
- Create: `frontend/src/pages/writing/CorrectionResult.tsx`
- Create: `frontend/src/pages/writing/CorrectionResult.css`
- Modify: `frontend/src/pages/writing/WritingEntryDetailPage.tsx`
- Modify: `frontend/src/pages/writing/WritingEntryDetailPage.css`

**Interfaces:**
- Consumes: `sanitizeMarkedText` (Task 3), `useGetWritingEntryQuery` + `WritingCorrectionDto` (Task 4), the existing `formatDateThai` from `./formatDate`.
- Produces: `<CorrectionResult correction={…} wordsPerMinute={…} elapsedSeconds={…} />`.

**The five blocks, in this exact order, all always present:**

| # | Heading | Body when populated | Body when empty |
|---|---|---|---|
| 1 | `เป้าหมายตอนนี้ · {targetRule}` | sanitized `markedText`, then the tally line `ต้องเติม {hit+miss} ที่ · ถูก {hit} · พลาด {miss}` | tally line still shows (zeros are truthful), plus `คืนนี้ไม่มีจุดไหนเข้ากฎนี้` when `hitCount + missCount === 0` |
| 2 | `ทำไม (ภาษาไทย)` | `thaiWhyLine` in full, never clamped | cannot be empty |
| 3 | `ต่อประโยค (จากประโยคของคุณเอง)` | one row per item: `source`, then `→ {combined}` | `คืนนี้ไม่มีประโยคอังกฤษให้ต่อ` |
| 4 | `คำที่นึกไม่ออก (จาก [วงเล็บ])` | one two-line card per item: `thai` above, `english` below | `คืนนี้ไม่มีคำในวงเล็บ` |
| 5 | `ตัวเลขวันนี้` | two tiles — `คำ/นาที` = `wordsPerMinute` to 1 dp with `{words} คำ ใน {elapsedSeconds} วินาที`; `พลาด/100 คำ` = `errorsPer100Words` to 1 dp with `เฉพาะกฎ {targetRule} เท่านั้น` | cannot be empty |

Below block 5, the forbidden-list note, verbatim:
`สิ่งที่ระบบจะไม่ทำเด็ดขาด — ไม่เขียนข้อความของคุณใหม่ · ไม่ให้คะแนน ไม่ชม · ไม่แก้ที่ผิดข้ออื่น · ไม่ให้แก้ข้อความเดิมแล้วตรวจซ้ำ`

The word count in block 5's caption is derived on the client purely for the caption — `Math.round(wordsPerMinute * elapsedSeconds / 60)`. It is a label, not a statistic; the statistic (`errorsPer100Words`) comes from the API.

- [ ] **Step 1: Write `CorrectionResult.tsx`**

```tsx
import type { ReactNode } from 'react'
import type { WritingCorrectionDto } from '../../shared/api/writingTypes'
import { sanitizeMarkedText } from './sanitizeMarkedText'
import './CorrectionResult.css'

interface Props {
  correction: WritingCorrectionDto
  wordsPerMinute: number
  elapsedSeconds: number
}

function Block({ n, title, children }: { n: number; title: string; children: ReactNode }) {
  return (
    <section className="correction-block">
      <p className="correction-block__head">
        <span className="correction-block__n">{n}</span>
        <span className="correction-block__title">{title}</span>
      </p>
      {children}
    </section>
  )
}

/**
 * The five fixed blocks of one night's Correction (ADR-178). All five render on
 * every corrected night, in this order: a block with no data states why it is
 * empty rather than disappearing, so the numbering is the same on every night
 * and "empty" is never mistaken for "the AI skipped it".
 */
export function CorrectionResult({ correction, wordsPerMinute, elapsedSeconds }: Props) {
  const {
    targetRule,
    markedText,
    hitCount,
    missCount,
    thaiWhyLine,
    sentenceCombiningItems,
    stuckWords,
    errorsPer100Words,
  } = correction

  const totalMarks = hitCount + missCount
  const wordCount = Math.round((wordsPerMinute * elapsedSeconds) / 60)

  return (
    <div className="correction-result">
      <Block n={1} title={`เป้าหมายตอนนี้ · ${targetRule}`}>
        {/* Sanitized above with a closed allow-list (ADR-180): p/span/br only,
            class restricted to miss|fix|hit|th. Never render markedText raw. */}
        <div
          className="correction-marked"
          dangerouslySetInnerHTML={{ __html: sanitizeMarkedText(markedText) }}
        />
        <p className="correction-tally">
          ต้องเติม {totalMarks} ที่ · ถูก {hitCount} · พลาด {missCount}
        </p>
        {totalMarks === 0 && <p className="correction-empty">คืนนี้ไม่มีจุดไหนเข้ากฎนี้</p>}
      </Block>

      <Block n={2} title="ทำไม (ภาษาไทย)">
        <div className="correction-why">{thaiWhyLine}</div>
      </Block>

      <Block n={3} title="ต่อประโยค (จากประโยคของคุณเอง)">
        {sentenceCombiningItems.length === 0 ? (
          <p className="correction-empty">คืนนี้ไม่มีประโยคอังกฤษให้ต่อ</p>
        ) : (
          <div className="correction-combine">
            {sentenceCombiningItems.map((item, i) => (
              <div key={i} className="correction-combine__item">
                <span className="correction-combine__src">{item.source}</span>
                <br />
                <span className="correction-combine__arrow">→</span> {item.combined}
              </div>
            ))}
          </div>
        )}
      </Block>

      <Block n={4} title="คำที่นึกไม่ออก (จาก [วงเล็บ])">
        {stuckWords.length === 0 ? (
          <p className="correction-empty">คืนนี้ไม่มีคำในวงเล็บ</p>
        ) : (
          <div className="correction-stuck">
            {stuckWords.map((word, i) => (
              <div key={i} className="correction-stuck__card">
                <div className="correction-stuck__thai">{word.thai}</div>
                <div className="correction-stuck__english">{word.english}</div>
              </div>
            ))}
          </div>
        )}
      </Block>

      <Block n={5} title="ตัวเลขวันนี้">
        <div className="correction-nums">
          <div className="correction-num">
            <div className="correction-num__key">คำ/นาที</div>
            <div className="correction-num__value">{wordsPerMinute.toFixed(1)}</div>
            <div className="correction-num__note">
              {wordCount} คำ ใน {elapsedSeconds} วินาที
            </div>
          </div>
          <div className="correction-num">
            <div className="correction-num__key">พลาด/100 คำ</div>
            <div className="correction-num__value">{errorsPer100Words.toFixed(1)}</div>
            <div className="correction-num__note">เฉพาะกฎ {targetRule} เท่านั้น</div>
          </div>
        </div>
      </Block>

      <div className="correction-never">
        <b>สิ่งที่ระบบจะไม่ทำเด็ดขาด</b> — ไม่เขียนข้อความของคุณใหม่ · ไม่ให้คะแนน ไม่ชม ·
        ไม่แก้ที่ผิดข้ออื่น · ไม่ให้แก้ข้อความเดิมแล้วตรวจซ้ำ
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Write `CorrectionResult.css`**

Ported from the mock card `screens/issue-97-correction-result.html`, using the app's own tokens from `frontend/src/index.css` (`--color-primary` is the same `#f57c00` the mock uses).

```css
.correction-result { display: block; }

.correction-block {
  border: 1px solid var(--color-border);
  border-radius: 10px;
  padding: 14px;
  margin-bottom: 12px;
}

.correction-block__head {
  display: flex;
  align-items: center;
  gap: 9px;
  margin: 0 0 10px;
}

.correction-block__n {
  flex: none;
  width: 21px;
  height: 21px;
  border-radius: 6px;
  background: var(--color-primary);
  color: #fff;
  font-size: 11.5px;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
}

.correction-block__title { font-size: 13.5px; font-weight: 700; }

.correction-marked { font-size: 14px; line-height: 2.05; }

/* The four marks a Correction may paint. Kept in sync with the allow-list in
   sanitizeMarkedText.ts -- a new mark needs both files. */
.correction-marked .miss {
  background: #ffe6e6;
  border-bottom: 2px solid var(--color-danger);
  padding: 0 2px;
  border-radius: 3px;
  font-weight: 600;
}
.correction-marked .fix { color: #2e7d32; font-weight: 700; font-size: 12.5px; }
.correction-marked .hit {
  background: #e8f5e9;
  border-bottom: 2px solid #2e7d32;
  padding: 0 2px;
  border-radius: 3px;
}
.correction-marked .th {
  background: #fff2d8;
  border-bottom: 1.5px dashed #b26a00;
  padding: 0 3px;
  border-radius: 3px;
}

.correction-tally { font-size: 11.5px; color: var(--color-text-muted); margin: 9px 0 0; }
.correction-empty { font-size: 12.5px; color: var(--color-text-muted); font-style: italic; margin: 6px 0 0; }

.correction-why {
  background: #fff3e0;
  border-left: 3px solid var(--color-primary);
  border-radius: 0 7px 7px 0;
  padding: 10px 12px;
  font-size: 13.5px;
  line-height: 1.7;
}

.correction-combine { font-size: 13.5px; display: grid; gap: 10px; }
.correction-combine__src { color: var(--color-text-muted); }
.correction-combine__arrow { color: var(--color-primary); font-weight: 700; }

.correction-stuck { display: grid; gap: 9px; }
.correction-stuck__card { border: 1px solid var(--color-border); border-radius: 9px; overflow: hidden; }
.correction-stuck__thai { padding: 9px 11px; font-size: 13px; background: #fffdf7; }
.correction-stuck__english {
  padding: 9px 11px;
  font-size: 13px;
  border-top: 1px solid var(--color-border);
  color: #2e7d32;
  font-weight: 600;
}

.correction-nums { display: grid; grid-template-columns: 1fr 1fr; gap: 11px; }
.correction-num { border: 1px solid var(--color-border); border-radius: 10px; padding: 13px; }
.correction-num__key { font-size: 11.5px; color: var(--color-text-muted); font-weight: 600; }
.correction-num__value {
  font-family: 'Consolas', ui-monospace, monospace;
  font-size: 26px;
  font-weight: 700;
  margin-top: 3px;
}
.correction-num__note { font-size: 11.5px; color: var(--color-text-muted); margin-top: 2px; }

.correction-never {
  margin-top: 12px;
  border: 1px dashed var(--color-border);
  border-radius: 9px;
  padding: 11px 13px;
  font-size: 12.5px;
  color: var(--color-text-muted);
}
.correction-never b { color: var(--color-danger); }

@media (max-width: 420px) {
  .correction-nums { grid-template-columns: 1fr; }
}
```

- [ ] **Step 3: Switch the detail page to the by-id query and the two states**

Replace `frontend/src/pages/writing/WritingEntryDetailPage.tsx` entirely:

```tsx
import { useEffect, useRef, useState } from 'react'
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
  useGetWritingEntryQuery,
  useUpdateWritingEntryTextMutation,
  useDeleteWritingEntryMutation,
} from '../../shared/api/api'
import { formatDateThai } from './formatDate'
import { saveErrorMessage } from './saveErrorMessage'
import { CorrectionResult } from './CorrectionResult'
import './WritingHistoryPage.css'
import './WritingEntryDetailPage.css'

export function WritingEntryDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [isEditing, setIsEditing] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Poll ONLY while the night is still pending: a correction can only arrive
  // over MCP, so an un-corrected page must notice it on its own (WMCP-26). Once
  // correctedAt is set the state is settled, and this payload carries a
  // markedText bounded at 50,000 characters — polling it forever buys nothing
  // (ADR-179). RTK Query honours a pollingInterval that changes between
  // renders, and 0 means "stop polling".
  const [pollingInterval, setPollingInterval] = useState(15_000)
  const { data: entry, isLoading, isError } = useGetWritingEntryQuery(id!, {
    skip: !id,
    pollingInterval,
  })

  const isLocked = Boolean(entry?.correctedAt)

  useEffect(() => {
    if (entry?.correctedAt) setPollingInterval(0)
  }, [entry?.correctedAt])
  const rteRef = useRef<RteInstance | null>(null)

  // A correction that lands while editing locks the text under us. Drop out of
  // edit mode immediately and say so — the writer's unsaved typing is lost,
  // which is the accepted trade (2026-08-17) for never offering an edit that
  // cannot be saved.
  useEffect(() => {
    if (isLocked && isEditing) {
      setIsEditing(false)
      setError('คืนนี้เพิ่งถูกตรวจแล้ว — แก้ข้อความไม่ได้อีก')
    }
  }, [isLocked, isEditing])

  const [updateText, { isLoading: isSaving }] = useUpdateWritingEntryTextMutation()
  const [deleteEntry, { isLoading: isDeleting }] = useDeleteWritingEntryMutation()

  const handleSave = async () => {
    if (!entry) return
    const html = rteRef.current?.getHtml() ?? ''
    setError(null)
    try {
      await updateText({ id: entry.id, text: html }).unwrap()
      setIsEditing(false)
    } catch (err) {
      console.error('updateWritingEntryText failed', err)
      // A correction can land while this PUT is in flight, so the page's own
      // correctedAt may still be stale here. The server's 400 is the only
      // signal that is never stale -- "try again" would be a lie.
      setError(saveErrorMessage(err))
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

  if (isError || !entry) {
    return (
      <div className="writing-detail-page">
        <button type="button" className="writing-detail-back-btn" onClick={() => navigate('/writing/history')}>
          ← กลับ
        </button>
        <div className="writing-detail-status">ไม่พบรายการนี้ (อาจถูกลบไปแล้ว)</div>
      </div>
    )
  }

  const deleteControls = confirmingDelete ? (
    <span className="writing-detail-confirm-delete">
      ลบรายการนี้แน่ใจไหม?
      <button type="button" className="writing-detail-confirm-yes" onClick={handleDelete} disabled={isDeleting}>
        ลบ
      </button>
      <button
        type="button"
        className="writing-detail-confirm-no"
        onClick={() => {
          setConfirmingDelete(false)
          setError(null)
        }}
      >
        ยกเลิก
      </button>
    </span>
  ) : (
    <button type="button" className="writing-detail-delete-btn" onClick={() => setConfirmingDelete(true)}>
      ลบ
    </button>
  )

  // Corrected: this page IS the ผลตรวจ (ADR-177). The raw text is not shown
  // again — block 1's marked text IS that text — and there is no edit button,
  // because a correction locks the text anyway (ADR-169).
  if (entry.correction) {
    return (
      <div className="writing-detail-page">
        <button type="button" className="writing-detail-back-btn" onClick={() => navigate('/writing/history')}>
          ← กลับ
        </button>
        <h1 className="writing-detail-result-title">ผลตรวจ · {formatDateThai(entry.date)}</h1>
        <CorrectionResult
          correction={entry.correction}
          wordsPerMinute={entry.wordsPerMinute}
          elapsedSeconds={entry.elapsedSeconds}
        />
        {error && <div className="writing-detail-error">{error}</div>}
        <div className="writing-detail-actions">{deleteControls}</div>
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
        <span className="writing-history-badge writing-history-badge--pending">⏳ รอตรวจ</span>
      </div>

      {isEditing ? (
        <RichTextEditorComponent
          ref={rteRef}
          height={300}
          value={entry.text}
          toolbarSettings={{ items: ['Bold', 'Italic', 'Underline', 'OrderedList', 'UnorderedList'] }}
        >
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
        {isEditing ? (
          <>
            <button type="button" className="writing-detail-save-btn" onClick={handleSave} disabled={isSaving}>
              บันทึก
            </button>
            <button
              type="button"
              className="writing-detail-cancel-btn"
              onClick={() => {
                setIsEditing(false)
                setError(null)
              }}
            >
              ยกเลิก
            </button>
          </>
        ) : (
          <button type="button" className="writing-detail-edit-btn" onClick={() => setIsEditing(true)}>
            แก้ไข
          </button>
        )}
        {deleteControls}
      </div>
    </div>
  )
}
```

**Note on the poll:** the interval lives in state so it can be switched to `0` the moment a correction appears — a hook cannot read the data it is about to fetch, so the effect is what closes the loop. At most one poll fires against a settled entry, and only if the page was opened on an already-corrected night before the effect runs.

- [ ] **Step 4: Add the result title style**

Append to `frontend/src/pages/writing/WritingEntryDetailPage.css`:

```css
.writing-detail-result-title {
  font-size: 17px;
  font-weight: 700;
  margin: 0 0 12px;
}
```

Then delete the now-unused `.writing-detail-locked-note` rule from that file — the locked note is gone with the corrected state's old layout.

- [ ] **Step 5: Type-check and build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: zero errors. `npm test` must still pass (`cd frontend && npm test`).

- [ ] **Step 6: Verify interactively — this step is not optional**

`tsc`, `build` and vitest cannot see a rendering bug (CLAUDE.md). Run the app and look at the screen:

```bash
cd frontend && npm run dev
```

Open `/writing/history`, tap the corrected night, and check against the mock card `screens/issue-97-correction-result.html` frame 1:
1. Header reads `ผลตรวจ · 16 สิงหาคม 2569`; there is **no** raw text block and **no** แก้ไข button.
2. All five numbered blocks are present, in order.
3. Block 1 shows the Thai bracket with the dashed amber underline, then `ต้องเติม 0 ที่ · ถูก 0 · พลาด 0`, then `คืนนี้ไม่มีจุดไหนเข้ากฎนี้`.
4. Block 2 shows the whole 389-character why-line, un-truncated, in the orange-left-border box.
5. Block 3 shows `คืนนี้ไม่มีประโยคอังกฤษให้ต่อ`.
6. Block 4 shows one two-line card: Thai on top, English below — not a pill.
7. Block 5 shows `5.9` and `0.0`.
8. The forbidden-list note sits below block 5, and ลบ still works.
9. At 380 px width nothing overflows horizontally.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/writing/CorrectionResult.tsx \
        frontend/src/pages/writing/CorrectionResult.css \
        frontend/src/pages/writing/WritingEntryDetailPage.tsx \
        frontend/src/pages/writing/WritingEntryDetailPage.css
git commit -m "feat(writing): render the five-block ผลตรวจ on the corrected night's page (#97)"
```

---

## Task 6: End-to-end coverage — and the spec this change breaks

`frontend/e2e/writing.live-lock.spec.ts` asserts on `ตรวจแล้ว — แก้ข้อความไม่ได้ (ลบทั้งรายการได้)`, which ADR-177 deletes, and it mocks only `**/api/writing-entries`, which does **not** match `/api/writing-entries/<id>` — so after Task 5 it fails twice over. This task repairs it and adds the rendering spec the new screen needs.

**Files:**
- Modify: `frontend/e2e/writing.live-lock.spec.ts`
- Create: `frontend/e2e/writing.correction-result.spec.ts`

**Interfaces:**
- Consumes: the rendered screen from Task 5 and the endpoint from Task 2.

- [ ] **Step 1: Repair the live-lock spec**

Replace `frontend/e2e/writing.live-lock.spec.ts` entirely:

```ts
import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

// The mid-edit lock defect (WMCP-26). A correction landing over MCP while the
// writer is editing used to leave Save enabled, fail the PUT, and show a
// "try again" message for something that can never succeed. This spec proves
// the live poll notices the correction and swaps the page to ผลตรวจ without a
// reload (ADR-177: the corrected page IS the result screen, so the old
// "ตรวจแล้ว — แก้ข้อความไม่ได้" note no longer exists).
const ENTRY_ID = '22222222-2222-2222-2222-222222222222'

const pending = {
  id: ENTRY_ID,
  date: '2026-08-16',
  text: '<p>Pending entry text.</p>',
  elapsedSeconds: 420,
  wordsPerMinute: 28,
  correctedAt: null,
  createdAt: '2026-08-16T09:00:00Z',
  correction: null,
}

const corrected = {
  ...pending,
  correctedAt: '2026-08-17T02:00:00Z',
  correction: {
    targetRule: 'articles (a/an/the)',
    markedText: '<p>Pending <span class="hit">entry</span> text.</p>',
    hitCount: 1,
    missCount: 0,
    thaiWhyLine: 'คำนามนับได้เอกพจน์ต้องมีตัวนำหน้าเสมอ',
    sentenceCombiningItems: [],
    stuckWords: [],
    errorsPer100Words: 0,
  },
}

test.describe('Writing — live lock while editing', () => {
  test('swaps to ผลตรวจ and drops Save when a correction lands mid-edit', async ({ authedPage: page }) => {
    let hasBeenCorrected = false
    await page.route(`**/api/writing-entries/${ENTRY_ID}`, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(hasBeenCorrected ? corrected : pending),
      })
    })

    await page.goto(`/writing/history/${ENTRY_ID}`)
    await page.getByRole('button', { name: 'แก้ไข' }).click()
    await expect(page.getByRole('button', { name: 'บันทึก' })).toBeVisible()

    // The correction lands over MCP; the page must notice on its own via the poll.
    hasBeenCorrected = true

    await expect(page.getByText('ผลตรวจ ·')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('button', { name: 'บันทึก' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'แก้ไข' })).toHaveCount(0)
    // Delete stays available even when locked (ADR-169).
    await expect(page.getByRole('button', { name: 'ลบ' })).toBeVisible()
    // No "try again" message should ever have appeared in this flow.
    await expect(page.getByText('ลองอีกครั้ง')).toHaveCount(0)
  })
})
```

- [ ] **Step 2: Run it and watch it fail before Task 5's code is in place**

Run: `cd frontend && npx playwright test e2e/writing.live-lock.spec.ts`
Expected (if run before Task 5): FAIL on `ผลตรวจ ·` never appearing. After Task 5: PASS.

- [ ] **Step 3: Write the rendering spec**

Create `frontend/e2e/writing.correction-result.spec.ts`:

```ts
import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

// Rendering coverage for the ผลตรวจ screen (ADR-177/178). The frontend unit
// suite runs in node with no DOM, so this spec is the only automated check that
// the five blocks actually render — the exact gap that shipped an unstyled RTE
// toolbar to prod on this same feature.
const THAI_ONLY_ID = '33333333-3333-3333-3333-333333333333'
const ENGLISH_ID = '44444444-4444-4444-4444-444444444444'

// The real production correction: a Thai-only night, nothing markable.
const thaiOnly = {
  id: THAI_ONLY_ID,
  date: '2026-08-16',
  text: '<p>[วันนี้พาลูกสาวไปกินข้าวเย็น]</p>',
  elapsedSeconds: 41,
  wordsPerMinute: 5.9,
  correctedAt: '2026-08-17T14:57:23Z',
  createdAt: '2026-08-16T15:00:00Z',
  correction: {
    targetRule: 'articles (a/an/the)',
    markedText: '<p><span class="th">[วันนี้พาลูกสาวไปกินข้าวเย็น]</span></p>',
    hitCount: 0,
    missCount: 0,
    thaiWhyLine: 'คำนามนับได้เอกพจน์ต้องมีตัวนำหน้าเสมอ ห้ามลอยเปล่า',
    sentenceCombiningItems: [],
    stuckWords: [
      { thai: 'วันนี้พาลูกสาวไปกินข้าวเย็น', english: 'Today I took my daughter out for dinner.' },
    ],
    errorsPer100Words: 0,
  },
}

const englishNight = {
  id: ENGLISH_ID,
  date: '2026-08-15',
  text: '<p>Today my daughter go to school.</p>',
  elapsedSeconds: 420,
  wordsPerMinute: 8.1,
  correctedAt: '2026-08-16T02:00:00Z',
  createdAt: '2026-08-15T15:00:00Z',
  correction: {
    targetRule: 'กริยาเติม -s',
    markedText:
      '<p>Today my daughter <span class="miss">go</span> <span class="fix">→ goes</span> to school.</p>',
    hitCount: 1,
    missCount: 8,
    thaiWhyLine: 'ประธานเป็น he / she / it → กริยาต้องเติม -s เสมอ',
    sentenceCombiningItems: [
      { source: 'Traffic is very bad. + We arrive late.', combined: 'Traffic was very bad, so we arrived late.' },
    ],
    stuckWords: [{ thai: 'ข้าวต้ม', english: 'rice porridge / congee' }],
    errorsPer100Words: 14,
  },
}

test.describe('Writing — ผลตรวจ screen', () => {
  test('a Thai-only night renders all five blocks, empty ones saying why', async ({ authedPage: page }) => {
    await page.route(`**/api/writing-entries/${THAI_ONLY_ID}`, async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(thaiOnly) })
    })

    await page.goto(`/writing/history/${THAI_ONLY_ID}`)

    await expect(page.getByText('ผลตรวจ ·')).toBeVisible()
    // The raw text block is gone: block 1's marked text IS that text (ADR-177).
    await expect(page.locator('.writing-detail-text')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'แก้ไข' })).toHaveCount(0)

    // All five blocks, in order.
    await expect(page.locator('.correction-block')).toHaveCount(5)
    await expect(page.getByText('เป้าหมายตอนนี้ · articles (a/an/the)')).toBeVisible()
    await expect(page.getByText('ทำไม (ภาษาไทย)')).toBeVisible()
    await expect(page.getByText('ต่อประโยค (จากประโยคของคุณเอง)')).toBeVisible()
    await expect(page.getByText('คำที่นึกไม่ออก (จาก [วงเล็บ])')).toBeVisible()
    await expect(page.getByText('ตัวเลขวันนี้')).toBeVisible()

    // The empty blocks say why they are empty rather than disappearing.
    await expect(page.getByText('ต้องเติม 0 ที่ · ถูก 0 · พลาด 0')).toBeVisible()
    await expect(page.getByText('คืนนี้ไม่มีจุดไหนเข้ากฎนี้')).toBeVisible()
    await expect(page.getByText('คืนนี้ไม่มีประโยคอังกฤษให้ต่อ')).toBeVisible()

    // The stuck word is a two-line card, not a pill.
    await expect(page.locator('.correction-stuck__thai')).toHaveText('วันนี้พาลูกสาวไปกินข้าวเย็น')
    await expect(page.locator('.correction-stuck__english')).toHaveText(
      'Today I took my daughter out for dinner.',
    )

    // The marked Thai bracket survived the sanitizer with its class intact.
    await expect(page.locator('.correction-marked span.th')).toHaveCount(1)

    await expect(page.getByText('5.9')).toBeVisible()
    await expect(page.getByText('0.0')).toBeVisible()
    await expect(page.getByText('สิ่งที่ระบบจะไม่ทำเด็ดขาด')).toBeVisible()
    await expect(page.getByRole('button', { name: 'ลบ' })).toBeVisible()
  })

  test('an English night renders the marks and the populated blocks', async ({ authedPage: page }) => {
    await page.route(`**/api/writing-entries/${ENGLISH_ID}`, async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(englishNight) })
    })

    await page.goto(`/writing/history/${ENGLISH_ID}`)

    await expect(page.locator('.correction-marked span.miss')).toHaveText('go')
    await expect(page.locator('.correction-marked span.fix')).toHaveText('→ goes')
    await expect(page.getByText('ต้องเติม 9 ที่ · ถูก 1 · พลาด 8')).toBeVisible()
    await expect(page.getByText('Traffic was very bad, so we arrived late.')).toBeVisible()
    await expect(page.locator('.correction-stuck__thai')).toHaveText('ข้าวต้ม')
    await expect(page.getByText('14.0')).toBeVisible()
    // No empty-state line should appear on a fully populated night.
    await expect(page.getByText('คืนนี้ไม่มีประโยคอังกฤษให้ต่อ')).toHaveCount(0)
  })

  test('a pending night still shows the text, the edit button and the badge', async ({ authedPage: page }) => {
    const pendingId = '55555555-5555-5555-5555-555555555555'
    await page.route(`**/api/writing-entries/${pendingId}`, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...thaiOnly, id: pendingId, correctedAt: null, correction: null }),
      })
    })

    await page.goto(`/writing/history/${pendingId}`)

    await expect(page.getByText('⏳ รอตรวจ')).toBeVisible()
    await expect(page.locator('.writing-detail-text')).toBeVisible()
    await expect(page.getByRole('button', { name: 'แก้ไข' })).toBeVisible()
    await expect(page.locator('.correction-block')).toHaveCount(0)
  })
})
```

- [ ] **Step 4: Run both specs**

Run: `cd frontend && npx playwright test e2e/writing.correction-result.spec.ts e2e/writing.live-lock.spec.ts`
Expected: PASS, 4 tests.

- [ ] **Step 5: Run the writing e2e set**

Run: `cd frontend && npx playwright test e2e/writing.history.spec.ts e2e/writing.target-rule.spec.ts e2e/writing.live-lock.spec.ts e2e/writing.correction-result.spec.ts`
Expected: PASS. (`e2e/writing.persistence.spec.ts` has two RED cases that pre-date this work — do not treat them as a regression, and do not fix them here.)

- [ ] **Step 6: Commit**

```bash
git add frontend/e2e/writing.live-lock.spec.ts frontend/e2e/writing.correction-result.spec.ts
git commit -m "test(writing): cover the ผลตรวจ render and repair the live-lock spec (#97)"
```

---

## After the last task

1. **Interactive check on the real device** — open the corrected night on the phone at 380 px and confirm nothing overflows and the why-line is fully readable.
2. **Push:** `git push main HEAD:main` (the remote is named `main`, not `origin`). This also carries the two already-committed but unpushed commits `37ec3e6` and `a6b5f8f`.
3. **No migration to apply.** Nothing in this plan changes the schema.
4. **Update the mock card** only if the built screen diverges from `screens/issue-97-correction-result.html` — the card is the agreed source of truth, so a deliberate divergence means re-pushing the card, not leaving them out of sync.
