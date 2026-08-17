# Writing-practice Phase 2 — WritingTools MCP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use sp-subagent-driven-development (recommended) or sp-executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the writing-practice AI correction loop over MCP — 4 tools the writer's personal Claude Code calls to find un-corrected nights, read/flip the active target grammar rule, and record a 5-block correction — plus the user-scoped rule storage that does not exist yet, the in-app rule control, and a fix for the mid-edit lock defect these tools arm.

**Architecture:** A new `WritingTools` `[McpServerToolType]` class mirrors `TripTools`: thin `[McpServerTool]` methods that delegate to Mediator commands/queries and nothing else. The active target rule lives on the existing `UserSettings` entity (1:1 with `User`, created lazily) but gets its **own** command rather than joining the full-snapshot `UpdateUserSettingsCommand` — an MCP caller only knows the rule and must not clear `HomePath`. Correction data lands in the seven Phase-2 columns already reserved on `WritingEntry`, so only one small migration is needed (the rule column).

**Tech Stack:** .NET 9, Mediator (source-generated), FluentValidation, EF Core (SQL Server prod / SQLite+InMemory tests), ModelContextProtocol C# SDK, xUnit + Moq + FluentAssertions, React 19 + RTK Query + Syncfusion, Playwright.

**Spec:**
- `docs/decision-map/writing-practice-build/map.md` (destination + out-of-scope)
- `docs/decision-map/writing-practice-build/tickets/mcp-tool-contract.md` (**the tool contract — read this first**)
- `docs/decision-map/writing-practice-build/tickets/ai-correction-invocation.md` (Path B, why MCP)
- `docs/decision-map/writing-practice-build/tickets/entry-mutability.md` + `docs/adr/169-a-corrected-entry-locks-a-deleted-entry-soft-deletes.md`
- `docs/decision-map/writing-practice-build/tickets/rule-rotation.md`, `tickets/pending-correction-visibility.md`
- Approved mock: Claude Design project `MenuNest design system` (id `8d8d4c81-41c1-4e0a-a0b7-370b39dfbe70`), `screens/writing-practice-critique-loop.html`, frame 2 (ผลตรวจ)
- Test suite this plan must satisfy: `docs/test-cases/writing-mcp-tools-test-cases.xlsx` (32 cases; case IDs `WMCP-nn` are referenced per task)

---

## Global Constraints

- **The 4 tool names are exact and the set is closed:** `list_pending_writing_entries`, `get_active_target_rule`, `set_active_target_rule`, `record_writing_correction`. Adding a 5th writing tool — especially any create/submit/edit-text tool — violates the contract (`mcp-tool-contract.md:38`). Entry creation stays in-app. (WMCP-12)
- **Every commit must leave the WHOLE suite green.** `frontend/.husky/pre-commit` runs backend `dotnet build` + `dotnet test` (Release) and frontend `tsc --noEmit` + `npm run build` on every commit (~40s+). Never `--no-verify`.
- **An entity/property change and its EF configuration land in the SAME commit.** An invalid model fails EF validation for every `DbContext` test (learned on #33).
- **`git add <explicit paths>` only** — never `git add -A`/`.`. `daily-state.md` (tracked, usually dirty) and `AGENTS.md` (untracked) must never enter a feature commit.
- **Every commit references the ticket:** `type(scope): summary (#97)`. The final commit of the last task may use `(closes #97)` only if the writer confirms the whole feature is done; otherwise keep `(#97)`.
- **Migrations are applied to prod BY HAND.** Neither the app nor CD runs `Migrate()`. A shipped-but-unapplied migration = feature-wide HTTP 500 `Invalid object name`/`Invalid column name` (bit #49). See CLAUDE.md for the exact `dotnet ef database update` command and the temporary-firewall dance.
- **Max lengths:** `ActiveTargetRule` 200 (matches `WritingEntries.TargetRule` `nvarchar(200)`, which snapshots it) · `MarkedText` 50,000 (matches the shipped `Text` ceiling) · `ThaiWhyLine` 2000 (**already configured** at `WritingEntryConfiguration.cs:24`).
- **Thai must round-trip un-escaped.** Serialize the JSON columns with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`; the default encoder would store `\u0E02…`. Verify by codepoint, never by console rendering. (WMCP-27)
- **Timestamps come from `IClock`**, never `DateTime.UtcNow`, in any handler (tests bind `FixedClock`). Domain entity methods already use `DateTime.UtcNow` directly — follow the entity's existing convention inside the entity, and pass the clock value in where a handler owns the timestamp.
- **A new `DbSet<>` must be added to all THREE `IApplicationDbContext` implementers** (`AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext`) or the build fails `CS0535`. *This plan adds no new DbSet* — `UserSettings` and `WritingEntries` both already exist on all three.
- **Frontend has no jsdom/RTL harness.** `tsc`/`build`/vitest cannot catch rendering. Any UI change is verified interactively and/or by a Playwright spec in `frontend/e2e/`.

### Decisions settled before planning (do not re-litigate)

| # | Question | Decision | Source |
|---|---|---|---|
| 1 | Correction lands while the writer is mid-edit | **Live lock**: the detail page polls; the moment `correctedAt` appears the edit UI locks and Save disappears. Typed-but-unsaved text is lost — accepted by the writer. | Writer, 2026-08-17 |
| 2 | Re-correcting an already-corrected night | **Overwrite, last-write-wins.** All 7 columns are replaced together. Still 4 tools. | Writer, 2026-08-17 |
| 3 | `get_active_target_rule` before any rule was ever set | **Return `null`**; Claude Code asks in chat and calls `set_active_target_rule` first. | Writer, 2026-08-17 |
| 4 | `markedText` ceiling | 50,000, matching the shipped `Text` ceiling. | Planner, follows precedent |
| 5 | errors-per-100-words rounding | **1 decimal place.** Confirmed by the mock: 8 misses / 57 words → `14.0`. | Mock frame 2 block 5 |
| 6 | `sentenceCombiningItems` count | **Accept 0–4; no minimum enforced.** The only real prod entry is Thai-only with no English sentences to combine — a hard minimum of 3 would break it. | Mock (3 items) + live prod row |
| 7 | Empty/whitespace rule | **Clears to `null` (unset)**, matching `SetHomePath`. | `UserSettings.cs:37-38` |

**Decision 2 does not contradict the mock's “ไม่ให้แก้ข้อความเดิมแล้วตรวจซ้ำ” / design-decision D (“no revision step exists”).** What the research forbids is *editing the text and re-correcting it* — a revision loop. ADR-169's text lock already makes that impossible: once `CorrectedAt` is set, `Text` is immutable. Decision 2 only permits re-running a correction over **unchanged, locked** text, which is repairing a bad AI pass, not revising the writing. A reviewer flagging this as a contradiction should be shown this paragraph.

### Explicitly OUT of scope for this plan

- The **ผลตรวจ** screen (mock frame 2) and the **ความคืบหน้า** screen (frame 3, 7-day pooled numbers, sparklines, monthly old-vs-new comparison). This plan *stores* the correction and exposes it over MCP; rendering it is the next plan. The one in-scope fragment of WMCP-24 is that `record_writing_correction` must accept **no** `wordsPerMinute`/`errorsPer100Words` argument.
- Restoring a soft-deleted entry, and draft autosave/crash-recovery — both still open fog on `map.md`.
- Any streak counter or numeric goal, anywhere (`map.md:40`).

### `markedText` storage convention

`markedText` is stored as the AI-produced string using the **mock's own inline HTML span convention**, because the mock is the render target and already defines the CSS classes:

- a miss: `<span class="miss">go</span> <span class="fix">→ goes</span>`
- a hit: `<span class="hit">is</span>`
- an untouched stuck-word bracket, preserved from the original: `<span class="th">[ข้าวต้ม]</span>`
- every other word copied through verbatim

Stored as-is (no server-side sanitising, matching how writer-authored RTE HTML is already stored). **The ผลตรวจ screen must whitelist these tags on render** — note this for that plan; it is not this plan's task.

---

## File Structure

**Backend — create:**
| Path | Responsibility |
|---|---|
| `backend/src/MenuNest.Application/UseCases/Writing/GetActiveTargetRule/GetActiveTargetRuleQuery.cs` | Query marker |
| `…/GetActiveTargetRule/GetActiveTargetRuleHandler.cs` | Reads `UserSettings.ActiveTargetRule` for the caller |
| `…/UseCases/Writing/SetActiveTargetRule/SetActiveTargetRuleCommand.cs` | Command carrying just the rule |
| `…/SetActiveTargetRule/SetActiveTargetRuleHandler.cs` | Lazily creates `UserSettings`, sets only the rule |
| `…/SetActiveTargetRule/SetActiveTargetRuleValidator.cs` | 200-char bound |
| `…/UseCases/Writing/ListPendingWritingEntries/ListPendingWritingEntriesQuery.cs` | Query marker |
| `…/ListPendingWritingEntries/ListPendingWritingEntriesHandler.cs` | `CorrectedAt == null && DeletedAt == null`, user-scoped, tie-broken by `CreatedAt` |
| `…/UseCases/Writing/RecordWritingCorrection/RecordWritingCorrectionCommand.cs` | The 5 blocks + entryId + targetRule |
| `…/RecordWritingCorrection/RecordWritingCorrectionHandler.cs` | Loads the entry, serialises JSON, calls the domain method |
| `…/RecordWritingCorrection/RecordWritingCorrectionValidator.cs` | Bounds on markedText / counts / items |
| `backend/src/MenuNest.McpServer/Tools/WritingTools.cs` | The 4 MCP tools — delegation only, no logic |
| `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<stamp>_AddUserSettingsActiveTargetRule.cs` | Generated by `dotnet ef migrations add` |

**Backend — modify:**
| Path | Change |
|---|---|
| `backend/src/MenuNest.Domain/Entities/UserSettings.cs` | `+ ActiveTargetRule` property, `+ SetActiveTargetRule()` |
| `backend/src/MenuNest.Domain/Entities/WritingEntry.cs` | `+ RecordCorrection(...)` |
| `backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserSettingsConfiguration.cs` | `+ HasMaxLength(200)` on the new column |
| `backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs` | `+ PendingWritingEntryDto`, `+ SentenceCombiningItemDto`, `+ StuckWordDto` |
| `backend/src/MenuNest.Application/UseCases/Me/MeDto.cs` | `+ ActiveTargetRule` |
| `backend/src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs` | Return the new field |
| `backend/src/MenuNest.WebApi/Controllers/MeController.cs` | `+ PUT /api/me/target-rule` |
| `backend/src/MenuNest.McpServer/McpServerRegistration.cs` | `+ .WithTools<Tools.WritingTools>()` |

**Backend — test (create):** `backend/tests/MenuNest.Application.UnitTests/Me/UserSettingsTests.cs`, `…/Writing/RecordCorrectionTests.cs` *(add to the existing `WritingEntryTests.cs` instead if it already covers the entity)*, `…/Writing/GetActiveTargetRuleHandlerTests.cs`, `…/Writing/SetActiveTargetRuleHandlerTests.cs`, `…/Writing/ListPendingWritingEntriesHandlerTests.cs`, `…/Writing/RecordWritingCorrectionHandlerTests.cs`, `backend/tests/MenuNest.McpServer.UnitTests/Tools/WritingToolsTests.cs`.

**Frontend — modify:** `frontend/src/shared/api/api.ts` (MeDto type, `setActiveTargetRule` mutation, writing-entries polling), `frontend/src/shared/hooks/useCurrentUser.ts`, `frontend/src/pages/settings/SettingsPage.tsx` (+ `.css`), `frontend/src/pages/writing/WritingEntryDetailPage.tsx` (+ `.css`).
**Frontend — create:** `frontend/src/pages/settings/targetRuleOptions.ts` (+ `.test.ts`), `frontend/e2e/writing.target-rule.spec.ts`, `frontend/e2e/writing.live-lock.spec.ts`.

---

## Task 1: Active-target-rule storage (domain + EF + migration)

Covers WMCP-08 (persistence), WMCP-21 (200-char bound), WMCP-22 (clearing).

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/UserSettings.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserSettingsConfiguration.cs:20`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<stamp>_AddUserSettingsActiveTargetRule.cs` (generated)
- Test: `backend/tests/MenuNest.Application.UnitTests/Me/UserSettingsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `UserSettings.ActiveTargetRule` (`string?`, get-only) and `UserSettings.SetActiveTargetRule(string? rule)` (`void`, throws `DomainException` over 200 chars, trims, treats null/whitespace as clear-to-null). Tasks 3 and 7 depend on both.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Me/UserSettingsTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Me;

public class UserSettingsTests
{
    [Fact]
    public void ActiveTargetRule_is_null_until_set()
    {
        var settings = UserSettings.Create(Guid.NewGuid());

        settings.ActiveTargetRule.Should().BeNull();
    }

    [Fact]
    public void SetActiveTargetRule_stores_the_trimmed_rule()
    {
        var settings = UserSettings.Create(Guid.NewGuid());

        settings.SetActiveTargetRule("  third-person singular -s  ");

        settings.ActiveTargetRule.Should().Be("third-person singular -s");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetActiveTargetRule_clears_to_null_for_blank_input(string? blank)
    {
        var settings = UserSettings.Create(Guid.NewGuid());
        settings.SetActiveTargetRule("articles (a/an/the)");

        settings.SetActiveTargetRule(blank);

        settings.ActiveTargetRule.Should().BeNull();
    }

    [Fact]
    public void SetActiveTargetRule_accepts_exactly_200_characters()
    {
        var settings = UserSettings.Create(Guid.NewGuid());

        settings.SetActiveTargetRule(new string('x', 200));

        settings.ActiveTargetRule!.Length.Should().Be(200);
    }

    [Fact]
    public void SetActiveTargetRule_rejects_201_characters()
    {
        var settings = UserSettings.Create(Guid.NewGuid());

        var act = () => settings.SetActiveTargetRule(new string('x', 201));

        act.Should().Throw<DomainException>()
            .WithMessage("ActiveTargetRule must be 200 characters or less.");
    }

    [Fact]
    public void SetActiveTargetRule_does_not_disturb_the_other_settings()
    {
        var settings = UserSettings.Create(Guid.NewGuid());
        settings.SetHomePath("/writing");
        settings.SetWeatherAlerts(8, 41);

        settings.SetActiveTargetRule("plural -s");

        settings.HomePath.Should().Be("/writing");
        settings.UvWarnThreshold.Should().Be(8);
        settings.FeelsLikeWarnThreshold.Should().Be(41);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~UserSettingsTests
```
Expected: FAIL to **compile** — `'UserSettings' does not contain a definition for 'ActiveTargetRule'` / `'SetActiveTargetRule'`.

- [ ] **Step 3: Add the property and mutator**

In `backend/src/MenuNest.Domain/Entities/UserSettings.cs`, after the `FeelsLikeWarnThreshold` property:

```csharp
    /// <summary>
    /// The one grammar rule the AI correction loop grades against, e.g.
    /// "third-person singular -s". Null = the writer has never chosen one;
    /// get_active_target_rule then returns null and Claude Code asks in chat
    /// before correcting (mcp-tool-contract). Flipped by hand, never on a
    /// calendar rotation (rule-rotation).
    /// </summary>
    public string? ActiveTargetRule { get; private set; }
```

and after `SetWeatherAlerts`:

```csharp
    /// <summary>
    /// Sets the active target grammar rule. Blank input clears it to unset,
    /// matching <see cref="SetHomePath"/>'s convention. Capped at 200 to match
    /// WritingEntry.TargetRule, which snapshots this value on every correction —
    /// a longer rule would set cleanly here and then fail when a correction
    /// copied it.
    /// </summary>
    public void SetActiveTargetRule(string? rule)
    {
        var trimmed = string.IsNullOrWhiteSpace(rule) ? null : rule.Trim();
        if (trimmed is not null && trimmed.Length > 200)
        {
            throw new DomainException("ActiveTargetRule must be 200 characters or less.");
        }

        ActiveTargetRule = trimmed;
        UpdatedAt = DateTime.UtcNow;
    }
```

- [ ] **Step 4: Add the EF configuration**

In `backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserSettingsConfiguration.cs`, directly below the `HomePath` line:

```csharp
        builder.Property(s => s.HomePath).HasMaxLength(100);
        // Matches WritingEntries.TargetRule's nvarchar(200) — a correction
        // snapshots this value onto the entry (mcp-tool-contract).
        builder.Property(s => s.ActiveTargetRule).HasMaxLength(200);
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~UserSettingsTests
```
Expected: PASS, 7 tests (the `[Theory]` contributes 3 cases).

- [ ] **Step 6: Generate the migration**

```bash
cd backend && dotnet ef migrations add AddUserSettingsActiveTargetRule \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Open the generated file and confirm it contains exactly one `AddColumn<string>` for `ActiveTargetRule` on `UserSettings` with `maxLength: 200, nullable: true`, and that `Down` drops only that column. If it contains anything else, the model has drifted — stop and report rather than editing the migration by hand.

- [ ] **Step 7: Run the FULL backend suite**

```bash
cd backend && dotnet test
```
Expected: PASS, 0 failures. A model-validation error here means the EF config and the entity disagree — fix before committing.

- [ ] **Step 8: Commit**

```bash
cd "c:/Repo2/t/menunest" && git add \
  backend/src/MenuNest.Domain/Entities/UserSettings.cs \
  backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserSettingsConfiguration.cs \
  backend/src/MenuNest.Infrastructure/Persistence/Migrations/ \
  backend/tests/MenuNest.Application.UnitTests/Me/UserSettingsTests.cs
git commit -m "feat(writing): store the active target grammar rule on UserSettings (#97)"
```

> **Do NOT apply the migration to prod yet.** It is applied once, after Task 7, in that task's final step.

---

## Task 2: `WritingEntry.RecordCorrection` (domain)

Covers WMCP-04 (all 7 columns), WMCP-15 (overwrite), WMCP-23 (WPM untouched), WMCP-07 (lock still holds).

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/WritingEntry.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Writing/WritingEntryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  ```csharp
  void RecordCorrection(
      DateTime correctedAtUtc, string targetRule, string markedText,
      int hitCount, int missCount, string thaiWhyLine,
      string sentenceCombiningItemsJson, string stuckWordsJson)
  ```
  Task 5's handler is the only caller. Overwrites all 7 columns on every call; leaves `Text` and `WordsPerMinute` untouched.

- [ ] **Step 1: Write the failing tests**

Append to `backend/tests/MenuNest.Application.UnitTests/Writing/WritingEntryTests.cs` (inside the existing class):

```csharp
    private static WritingEntry APendingEntry() =>
        WritingEntry.Create(Guid.NewGuid(), new DateOnly(2026, 8, 16), "<p>She go to school.</p>", 420);

    [Fact]
    public void RecordCorrection_sets_every_correction_column()
    {
        var entry = APendingEntry();
        var at = new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc);

        entry.RecordCorrection(
            correctedAtUtc: at,
            targetRule: "third-person singular -s",
            markedText: "<p>She <span class=\"miss\">go</span> <span class=\"fix\">→ goes</span> to school.</p>",
            hitCount: 0,
            missCount: 1,
            thaiWhyLine: "ประธานเป็น he / she / it → กริยาต้องเติม -s",
            sentenceCombiningItemsJson: "[]",
            stuckWordsJson: "[]");

        entry.CorrectedAt.Should().Be(at);
        entry.TargetRule.Should().Be("third-person singular -s");
        entry.MarkedText.Should().Contain("→ goes");
        entry.HitCount.Should().Be(0);
        entry.MissCount.Should().Be(1);
        entry.ThaiWhyLine.Should().Contain("เติม -s");
        entry.SentenceCombiningItemsJson.Should().Be("[]");
        entry.StuckWordsJson.Should().Be("[]");
    }

    [Fact]
    public void RecordCorrection_leaves_the_text_and_words_per_minute_untouched()
    {
        var entry = APendingEntry();
        var originalText = entry.Text;
        var originalWpm = entry.WordsPerMinute;

        entry.RecordCorrection(
            new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc),
            "third-person singular -s",
            new string('x', 5_000),   // a marked text far longer than the original
            0, 1, "เหตุผล", "[]", "[]");

        entry.Text.Should().Be(originalText);
        entry.WordsPerMinute.Should().Be(originalWpm);
    }

    [Fact]
    public void RecordCorrection_a_second_time_overwrites_every_column_together()
    {
        var entry = APendingEntry();
        var first = new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);
        var second = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);

        entry.RecordCorrection(first, "third-person singular -s", "first", 0, 3, "แรก", "[]", "[]");
        entry.RecordCorrection(second, "plural -s", "second", 1, 2, "สอง", "[{\"a\":1}]", "[{\"b\":2}]");

        // Last write wins, and no column is left describing the previous pass.
        entry.CorrectedAt.Should().Be(second);
        entry.TargetRule.Should().Be("plural -s");
        entry.MarkedText.Should().Be("second");
        entry.HitCount.Should().Be(1);
        entry.MissCount.Should().Be(2);
        entry.ThaiWhyLine.Should().Be("สอง");
        entry.SentenceCombiningItemsJson.Should().Be("[{\"a\":1}]");
        entry.StuckWordsJson.Should().Be("[{\"b\":2}]");
    }

    [Fact]
    public void RecordCorrection_still_locks_the_text_against_editing()
    {
        var entry = APendingEntry();
        entry.RecordCorrection(
            new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc),
            "third-person singular -s", "marked", 0, 1, "เหตุผล", "[]", "[]");

        var act = () => entry.UpdateText("<p>edited after the correction</p>");

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot edit text after a correction has been recorded.");
    }

    [Fact]
    public void RecordCorrection_rejects_negative_counts()
    {
        var entry = APendingEntry();

        var act = () => entry.RecordCorrection(
            new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc),
            "third-person singular -s", "marked", -1, 0, "เหตุผล", "[]", "[]");

        act.Should().Throw<DomainException>()
            .WithMessage("HitCount and MissCount cannot be negative.");
    }

    [Fact]
    public void RecordCorrection_requires_a_target_rule()
    {
        var entry = APendingEntry();

        var act = () => entry.RecordCorrection(
            new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc),
            "   ", "marked", 0, 1, "เหตุผล", "[]", "[]");

        act.Should().Throw<DomainException>()
            .WithMessage("TargetRule is required to record a correction.");
    }

    [Fact]
    public void RecordCorrection_is_allowed_on_a_soft_deleted_entry_only_via_the_handler_guard()
    {
        // The entity itself does not block it — the handler's DeletedAt == null
        // filter is what refuses (WMCP-14). This test documents that boundary so
        // nobody "fixes" it in the wrong layer.
        var entry = APendingEntry();
        entry.SoftDelete();

        var act = () => entry.RecordCorrection(
            new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc),
            "third-person singular -s", "marked", 0, 1, "เหตุผล", "[]", "[]");

        act.Should().NotThrow();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~WritingEntryTests
```
Expected: FAIL to compile — no `RecordCorrection`, no `MarkedText`.

- [ ] **Step 3: Add `MarkedText` and `RecordCorrection`**

In `backend/src/MenuNest.Domain/Entities/WritingEntry.cs`, add `MarkedText` to the Phase-2 block (right after `CorrectedAt`):

```csharp
    public DateTime? CorrectedAt { get; private set; }
    public string? MarkedText { get; private set; }
    public string? TargetRule { get; private set; }
```

and add the method after `UpdateText`:

```csharp
    /// <summary>
    /// Records (or re-records) the AI correction for this entry — the five
    /// blocks of mcp-tool-contract's record_writing_correction. Called only
    /// from RecordWritingCorrectionHandler, which owns the DeletedAt guard and
    /// the clock.
    ///
    /// Re-recording OVERWRITES all seven columns together (writer's decision,
    /// 2026-08-17): a bad AI pass must be repairable. That is not the revision
    /// loop the mock forbids — UpdateText stays locked, so the corrected text
    /// itself can never drift.
    ///
    /// Text and WordsPerMinute are deliberately untouched: WordsPerMinute
    /// measures the original timed session, and markedText is strictly longer
    /// than the text it annotates.
    /// </summary>
    public void RecordCorrection(
        DateTime correctedAtUtc,
        string targetRule,
        string markedText,
        int hitCount,
        int missCount,
        string thaiWhyLine,
        string sentenceCombiningItemsJson,
        string stuckWordsJson)
    {
        if (string.IsNullOrWhiteSpace(targetRule))
            throw new DomainException("TargetRule is required to record a correction.");
        if (hitCount < 0 || missCount < 0)
            throw new DomainException("HitCount and MissCount cannot be negative.");

        CorrectedAt = correctedAtUtc;
        TargetRule = targetRule.Trim();
        MarkedText = markedText;
        HitCount = hitCount;
        MissCount = missCount;
        ThaiWhyLine = thaiWhyLine;
        SentenceCombiningItemsJson = sentenceCombiningItemsJson;
        StuckWordsJson = stuckWordsJson;
        UpdatedAt = DateTime.UtcNow;
    }
```

- [ ] **Step 4: Configure and migrate the new `MarkedText` column**

In `backend/src/MenuNest.Infrastructure/Persistence/Configurations/WritingEntryConfiguration.cs`, in the Phase-2 block:

```csharp
        builder.Property(w => w.TargetRule).HasMaxLength(200);
        builder.Property(w => w.MarkedText).HasMaxLength(50_000);
        builder.Property(w => w.ThaiWhyLine).HasMaxLength(2000);
```

```bash
cd backend && dotnet ef migrations add AddWritingEntryMarkedText \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Confirm it adds exactly one nullable `MarkedText` column with `maxLength: 50000`.

- [ ] **Step 5: Run the full backend suite**

```bash
cd backend && dotnet test
```
Expected: PASS, 0 failures.

- [ ] **Step 6: Commit**

```bash
cd "c:/Repo2/t/menunest" && git add \
  backend/src/MenuNest.Domain/Entities/WritingEntry.cs \
  backend/src/MenuNest.Infrastructure/Persistence/Configurations/WritingEntryConfiguration.cs \
  backend/src/MenuNest.Infrastructure/Persistence/Migrations/ \
  backend/tests/MenuNest.Application.UnitTests/Writing/WritingEntryTests.cs
git commit -m "feat(writing): WritingEntry.RecordCorrection with overwrite semantics (#97)"
```

---

## Task 3: Get/set the active target rule (application layer)

Covers WMCP-02 (null when unset), WMCP-08, WMCP-21, WMCP-22, and the "a settings save must not clear the rule" guarantee.

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Writing/GetActiveTargetRule/GetActiveTargetRuleQuery.cs`
- Create: `…/GetActiveTargetRule/GetActiveTargetRuleHandler.cs`
- Create: `…/SetActiveTargetRule/SetActiveTargetRuleCommand.cs`
- Create: `…/SetActiveTargetRule/SetActiveTargetRuleHandler.cs`
- Create: `…/SetActiveTargetRule/SetActiveTargetRuleValidator.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Writing/ActiveTargetRuleHandlerTests.cs`

**Interfaces:**
- Consumes: `UserSettings.SetActiveTargetRule` / `.ActiveTargetRule` (Task 1).
- Produces:
  - `GetActiveTargetRuleQuery() : IQuery<string?>`
  - `SetActiveTargetRuleCommand(string? Rule) : ICommand<string?>` — returns the stored value after the write.
  Tasks 6 (MCP) and 7 (WebApi) both send these.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Writing/ActiveTargetRuleHandlerTests.cs`:

```csharp
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Me.UpdateUserSettings;
using MenuNest.Application.UseCases.Writing.GetActiveTargetRule;
using MenuNest.Application.UseCases.Writing.SetActiveTargetRule;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Writing;

public class ActiveTargetRuleHandlerTests
{
    private static SetActiveTargetRuleHandler SetHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new SetActiveTargetRuleValidator());

    private static GetActiveTargetRuleHandler GetHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    [Fact]
    public async Task Get_returns_null_when_no_settings_row_exists_at_all()
    {
        using var fx = new HandlerTestFixture();

        var rule = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);

        rule.Should().BeNull();
    }

    [Fact]
    public async Task Set_creates_the_settings_row_lazily_then_get_reads_it_back()
    {
        using var fx = new HandlerTestFixture();

        var written = await SetHandler(fx).Handle(
            new SetActiveTargetRuleCommand("third-person singular -s"), CancellationToken.None);
        var read = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);

        written.Should().Be("third-person singular -s");
        read.Should().Be("third-person singular -s");
    }

    [Fact]
    public async Task Set_overwrites_a_previous_rule()
    {
        using var fx = new HandlerTestFixture();
        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("articles (a/an/the)"), CancellationToken.None);

        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("past simple -ed"), CancellationToken.None);

        var read = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);
        read.Should().Be("past simple -ed");
    }

    [Fact]
    public async Task Set_with_blank_clears_the_rule()
    {
        using var fx = new HandlerTestFixture();
        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("plural -s"), CancellationToken.None);

        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("   "), CancellationToken.None);

        var read = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);
        read.Should().BeNull();
    }

    [Fact]
    public async Task Set_rejects_a_rule_over_200_characters()
    {
        using var fx = new HandlerTestFixture();

        var act = async () => await SetHandler(fx).Handle(
            new SetActiveTargetRuleCommand(new string('x', 201)), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Set_does_not_clear_the_home_path_or_weather_thresholds()
    {
        using var fx = new HandlerTestFixture();
        var settingsHandler = new UpdateUserSettingsHandler(
            fx.Db, fx.UserProvisioner.Object, new UpdateUserSettingsValidator());
        await settingsHandler.Handle(new UpdateUserSettingsCommand("/writing", 8, 41), CancellationToken.None);

        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("plural -s"), CancellationToken.None);

        var settings = fx.Db.UserSettings.Single(s => s.UserId == fx.User.Id);
        settings.HomePath.Should().Be("/writing");
        settings.UvWarnThreshold.Should().Be(8);
        settings.FeelsLikeWarnThreshold.Should().Be(41);
        settings.ActiveTargetRule.Should().Be("plural -s");
    }

    [Fact]
    public async Task A_settings_save_does_not_clear_an_existing_rule()
    {
        // UpdateUserSettings is a full-snapshot PUT (ADR-091). The rule is
        // deliberately NOT part of that snapshot, so saving Home/weather from
        // the settings screen must leave the rule alone.
        using var fx = new HandlerTestFixture();
        await SetHandler(fx).Handle(new SetActiveTargetRuleCommand("plural -s"), CancellationToken.None);
        var settingsHandler = new UpdateUserSettingsHandler(
            fx.Db, fx.UserProvisioner.Object, new UpdateUserSettingsValidator());

        await settingsHandler.Handle(new UpdateUserSettingsCommand("/budget", 6, 40), CancellationToken.None);

        var read = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);
        read.Should().Be("plural -s");
    }

    [Fact]
    public async Task Get_is_scoped_to_the_calling_user()
    {
        using var fx = new HandlerTestFixture();
        var otherSettings = UserSettings.Create(Guid.NewGuid());
        otherSettings.SetActiveTargetRule("someone elses rule");
        fx.Db.UserSettings.Add(otherSettings);
        await fx.Db.SaveChangesAsync();

        var read = await GetHandler(fx).Handle(new GetActiveTargetRuleQuery(), CancellationToken.None);

        read.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~ActiveTargetRuleHandlerTests
```
Expected: FAIL to compile — the query/command/handler types do not exist.

- [ ] **Step 3: Create the query and its handler**

`backend/src/MenuNest.Application/UseCases/Writing/GetActiveTargetRule/GetActiveTargetRuleQuery.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Writing.GetActiveTargetRule;

/// <summary>
/// Returns the caller's active target grammar rule, or null when they have
/// never set one — Claude Code then asks in chat and calls
/// set_active_target_rule before correcting (mcp-tool-contract).
/// </summary>
public sealed record GetActiveTargetRuleQuery : IQuery<string?>;
```

`…/GetActiveTargetRule/GetActiveTargetRuleHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.GetActiveTargetRule;

public sealed class GetActiveTargetRuleHandler : IQueryHandler<GetActiveTargetRuleQuery, string?>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetActiveTargetRuleHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<string?> Handle(GetActiveTargetRuleQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var settings = await _db.UserSettings
            .FirstOrDefaultAsync(s => s.UserId == user.Id, ct);

        return settings?.ActiveTargetRule;
    }
}
```

- [ ] **Step 4: Create the command, validator and handler**

`…/SetActiveTargetRule/SetActiveTargetRuleCommand.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Writing.SetActiveTargetRule;

/// <summary>
/// Changes the caller's active target grammar rule. Deliberately NOT part of
/// UpdateUserSettingsCommand's full snapshot (ADR-091): an MCP caller knows
/// only the rule and must not clear HomePath or the weather thresholds.
/// Blank clears the rule. Returns the stored value.
/// </summary>
public sealed record SetActiveTargetRuleCommand(string? Rule) : ICommand<string?>;
```

`…/SetActiveTargetRule/SetActiveTargetRuleValidator.cs`:

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.SetActiveTargetRule;

public sealed class SetActiveTargetRuleValidator : AbstractValidator<SetActiveTargetRuleCommand>
{
    public SetActiveTargetRuleValidator()
    {
        // Blank is legal (it clears the rule). Only the ceiling is enforced —
        // 200 to match WritingEntries.TargetRule, which snapshots it.
        RuleFor(x => x.Rule)
            .MaximumLength(200).WithMessage("Rule must be 200 characters or less.");
    }
}
```

`…/SetActiveTargetRule/SetActiveTargetRuleHandler.cs`:

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.SetActiveTargetRule;

public sealed class SetActiveTargetRuleHandler : ICommandHandler<SetActiveTargetRuleCommand, string?>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<SetActiveTargetRuleCommand> _validator;

    public SetActiveTargetRuleHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<SetActiveTargetRuleCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<string?> Handle(SetActiveTargetRuleCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == user.Id, ct);
        if (settings is null)
        {
            settings = UserSettings.Create(user.Id);
            _db.UserSettings.Add(settings);
        }

        // Only the rule — HomePath and the weather thresholds are untouched.
        settings.SetActiveTargetRule(command.Rule);
        await _db.SaveChangesAsync(ct);

        return settings.ActiveTargetRule;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~ActiveTargetRuleHandlerTests
```
Expected: PASS, 8 tests.

- [ ] **Step 6: Run the full backend suite**

```bash
cd backend && dotnet test
```
Expected: PASS, 0 failures. (Mediator is source-generated — a handler that does not match its command/query shape fails the build here.)

- [ ] **Step 7: Commit**

```bash
cd "c:/Repo2/t/menunest" && git add \
  backend/src/MenuNest.Application/UseCases/Writing/GetActiveTargetRule/ \
  backend/src/MenuNest.Application/UseCases/Writing/SetActiveTargetRule/ \
  backend/tests/MenuNest.Application.UnitTests/Writing/ActiveTargetRuleHandlerTests.cs
git commit -m "feat(writing): get/set the active target rule as its own use case (#97)"
```

---

## Task 4: `list_pending_writing_entries` use case

Covers WMCP-03 (5 contract fields), WMCP-05 (corrected disappears), WMCP-11 (user scope), WMCP-13 (soft-deleted excluded), WMCP-17 (CreatedAt tie-break regression).

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Writing/ListPendingWritingEntries/ListPendingWritingEntriesQuery.cs`
- Create: `…/ListPendingWritingEntries/ListPendingWritingEntriesHandler.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Writing/ListPendingWritingEntriesHandlerTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `PendingWritingEntryDto(Guid Id, DateOnly Date, string Text, int ElapsedSeconds, double WordsPerMinute)` — exactly the contract's 5 fields, no `correctedAt` (every row is pending by definition).
  - `ListPendingWritingEntriesQuery() : IQuery<IReadOnlyList<PendingWritingEntryDto>>`
  Task 6 sends this.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Writing/ListPendingWritingEntriesHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.ListPendingWritingEntries;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Writing;

public class ListPendingWritingEntriesHandlerTests
{
    private static readonly DateTime CorrectedAt = new(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);

    private static void Correct(WritingEntry e) =>
        e.RecordCorrection(CorrectedAt, "third-person singular -s", "marked", 0, 1, "เหตุผล", "[]", "[]");

    [Fact]
    public async Task Returns_only_entries_with_no_correction_yet()
    {
        using var fx = new HandlerTestFixture();
        var handler = new ListPendingWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var pending = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>pending night here</p>", 41);
        var corrected = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 15), "<p>corrected night here</p>", 420);
        Correct(corrected);
        fx.Db.WritingEntries.AddRange(pending, corrected);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListPendingWritingEntriesQuery(), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(pending.Id);
    }

    [Fact]
    public async Task Carries_the_five_contract_fields_with_the_real_computed_wpm()
    {
        using var fx = new HandlerTestFixture();
        var handler = new ListPendingWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        // Mirrors the real prod row: 4 whitespace tokens over 41 seconds.
        var entry = WritingEntry.Create(
            fx.User.Id, new DateOnly(2026, 8, 16), "<p>[หนึ่ง สอง สาม passione]</p>", 41);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListPendingWritingEntriesQuery(), CancellationToken.None);

        var dto = result.Single();
        dto.Id.Should().Be(entry.Id);
        dto.Date.Should().Be(new DateOnly(2026, 8, 16));
        dto.Text.Should().Be("<p>[หนึ่ง สอง สาม passione]</p>");
        dto.ElapsedSeconds.Should().Be(41);
        dto.WordsPerMinute.Should().BeApproximately(4 / (41 / 60.0), 0.000001);
    }

    [Fact]
    public async Task Excludes_soft_deleted_entries_even_when_still_pending()
    {
        using var fx = new HandlerTestFixture();
        var handler = new ListPendingWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var deletedPending = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 14), "<p>deleted and pending</p>", 420);
        deletedPending.SoftDelete();
        fx.Db.WritingEntries.Add(deletedPending);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListPendingWritingEntriesQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Excludes_other_users_pending_entries()
    {
        using var fx = new HandlerTestFixture();
        var handler = new ListPendingWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var other = User.CreateFromExternalLogin("other-oid", "other@example.com", "Other", AuthProvider.Microsoft);
        fx.Db.Users.Add(other);
        fx.Db.WritingEntries.Add(
            WritingEntry.Create(other.Id, new DateOnly(2026, 8, 16), "<p>not mine at all</p>", 420));
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListPendingWritingEntriesQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Orders_newest_date_first_and_breaks_same_date_ties_by_creation_time()
    {
        // Regression: commit 5b4b56d added this tie-break to ListWritingEntries.
        // A re-implemented pending query must not lose it.
        using var fx = new HandlerTestFixture();
        var handler = new ListPendingWritingEntriesHandler(fx.Db, fx.UserProvisioner.Object);

        var sameDate = new DateOnly(2026, 8, 16);
        var earlier = WritingEntry.Create(fx.User.Id, sameDate, "<p>earlier sitting today</p>", 420);
        var later = WritingEntry.Create(fx.User.Id, sameDate, "<p>later sitting today</p>", 420);
        SetCreatedAt(earlier, new DateTime(2026, 8, 16, 20, 0, 0, DateTimeKind.Utc));
        SetCreatedAt(later, new DateTime(2026, 8, 16, 22, 0, 0, DateTimeKind.Utc));
        var older = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 10), "<p>an older night here</p>", 420);

        fx.Db.WritingEntries.AddRange(earlier, later, older);
        await fx.Db.SaveChangesAsync();

        var result = await handler.Handle(new ListPendingWritingEntriesQuery(), CancellationToken.None);

        result.Select(r => r.Id).Should().ContainInOrder(later.Id, earlier.Id, older.Id);
    }

    // CreatedAt is set by the Entity base class; reflection is how the existing
    // ListWritingEntriesHandlerTests controls it (see its `using System.Reflection`).
    private static void SetCreatedAt(WritingEntry entry, DateTime value) =>
        typeof(MenuNest.Domain.Common.Entity)
            .GetProperty(nameof(MenuNest.Domain.Common.Entity.CreatedAt))!
            .SetValue(entry, value);
}
```

> If `Entity.CreatedAt` has no settable accessor reachable this way, copy the exact mechanism used by the existing `ListWritingEntriesHandlerTests.Same_date_entries_are_ordered_by_creation_time_newest_first` test — it already solves this problem in this repo. Do not change the entity to make the test easier.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~ListPendingWritingEntriesHandlerTests
```
Expected: FAIL to compile — the query, handler and DTO do not exist.

- [ ] **Step 3: Add the DTO**

Append to `backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs`:

```csharp
/// <summary>
/// One un-corrected night, as returned by list_pending_writing_entries.
/// Exactly the five fields of mcp-tool-contract:51-53 — no CorrectedAt,
/// because every row in this list is pending by definition.
/// </summary>
public sealed record PendingWritingEntryDto(
    Guid Id,
    DateOnly Date,
    string Text,
    int ElapsedSeconds,
    double WordsPerMinute);
```

- [ ] **Step 4: Create the query and handler**

`…/ListPendingWritingEntries/ListPendingWritingEntriesQuery.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Writing.ListPendingWritingEntries;

/// <summary>
/// Every entry of the current user that has no correction yet, newest first.
/// This is how Claude Code answers "did I write anything since the last
/// correction?" without the writer naming a date (mcp-tool-contract).
/// </summary>
public sealed record ListPendingWritingEntriesQuery : IQuery<IReadOnlyList<PendingWritingEntryDto>>;
```

`…/ListPendingWritingEntries/ListPendingWritingEntriesHandler.cs`:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.ListPendingWritingEntries;

public sealed class ListPendingWritingEntriesHandler
    : IQueryHandler<ListPendingWritingEntriesQuery, IReadOnlyList<PendingWritingEntryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListPendingWritingEntriesHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<PendingWritingEntryDto>> Handle(
        ListPendingWritingEntriesQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        return await _db.WritingEntries
            .Where(w => w.UserId == user.Id && w.DeletedAt == null && w.CorrectedAt == null)
            // Same ordering as ListWritingEntries, incl. the CreatedAt tie-break
            // added in 5b4b56d — two sittings on one date must be stably ordered.
            .OrderByDescending(w => w.Date)
            .ThenByDescending(w => w.CreatedAt)
            .Select(w => new PendingWritingEntryDto(
                w.Id, w.Date, w.Text, w.ElapsedSeconds, w.WordsPerMinute))
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~ListPendingWritingEntriesHandlerTests
```
Expected: PASS, 5 tests.

- [ ] **Step 6: Run the full backend suite, then commit**

```bash
cd backend && dotnet test
cd "c:/Repo2/t/menunest" && git add \
  backend/src/MenuNest.Application/UseCases/Writing/ListPendingWritingEntries/ \
  backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs \
  backend/tests/MenuNest.Application.UnitTests/Writing/ListPendingWritingEntriesHandlerTests.cs
git commit -m "feat(writing): ListPendingWritingEntries query for the MCP correction loop (#97)"
```

---

## Task 5: `record_writing_correction` use case

Covers WMCP-04, WMCP-14 (deleted refused), WMCP-15 (overwrite), WMCP-16 (unknown id message), WMCP-19 (markedText cap), WMCP-25 (Thai-only 0/0), WMCP-27 (Thai JSON round-trip), WMCP-28 (0–4 items).

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Writing/RecordWritingCorrection/RecordWritingCorrectionCommand.cs`
- Create: `…/RecordWritingCorrection/RecordWritingCorrectionHandler.cs`
- Create: `…/RecordWritingCorrection/RecordWritingCorrectionValidator.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Writing/RecordWritingCorrectionHandlerTests.cs`

**Interfaces:**
- Consumes: `WritingEntry.RecordCorrection` (Task 2).
- Produces:
  - `SentenceCombiningItemDto(string Source, string Combined)` — the mock's block 3 shape (`"Traffic is very bad. + We arrive late."` → `"Traffic was very bad, so we arrived late."`).
  - `StuckWordDto(string Thai, string English)` — the mock's block 4 shape (`ข้าวต้ม → rice porridge / congee`).
  - `RecordWritingCorrectionCommand(Guid EntryId, string TargetRule, string MarkedText, int HitCount, int MissCount, string ThaiWhyLine, IReadOnlyList<SentenceCombiningItemDto> SentenceCombiningItems, IReadOnlyList<StuckWordDto> StuckWords) : ICommand<WritingEntryDto>`
  Task 6 sends this.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.Application.UnitTests/Writing/RecordWritingCorrectionHandlerTests.cs`:

```csharp
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.RecordWritingCorrection;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Writing;

public class RecordWritingCorrectionHandlerTests
{
    private static RecordWritingCorrectionHandler Handler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new RecordWritingCorrectionValidator(), fx.Clock);

    private static RecordWritingCorrectionCommand ACommand(
        Guid entryId,
        int hit = 0,
        int miss = 1,
        IReadOnlyList<SentenceCombiningItemDto>? items = null,
        IReadOnlyList<StuckWordDto>? stuck = null,
        string? markedText = null) =>
        new(
            EntryId: entryId,
            TargetRule: "third-person singular -s",
            MarkedText: markedText ?? "<p>She <span class=\"miss\">go</span> <span class=\"fix\">→ goes</span> to school.</p>",
            HitCount: hit,
            MissCount: miss,
            ThaiWhyLine: "ประธานเป็น he / she / it → กริยาต้องเติม -s",
            SentenceCombiningItems: items ?? new List<SentenceCombiningItemDto>(),
            StuckWords: stuck ?? new List<StuckWordDto>());

    private static async Task<WritingEntry> SeedPending(HandlerTestFixture fx)
    {
        var entry = WritingEntry.Create(fx.User.Id, new DateOnly(2026, 8, 16), "<p>She go to school.</p>", 420);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task Records_the_correction_and_stamps_CorrectedAt_from_the_clock()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc);
        var entry = await SeedPending(fx);

        var dto = await Handler(fx).Handle(ACommand(entry.Id), CancellationToken.None);

        dto.CorrectedAt.Should().Be(new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc));
        var saved = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        saved.TargetRule.Should().Be("third-person singular -s");
        saved.HitCount.Should().Be(0);
        saved.MissCount.Should().Be(1);
        saved.MarkedText.Should().Contain("→ goes");
        saved.ThaiWhyLine.Should().Contain("เติม -s");
    }

    [Fact]
    public async Task Serialises_the_two_json_blocks_with_unescaped_thai()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);

        await Handler(fx).Handle(
            ACommand(
                entry.Id,
                items: new List<SentenceCombiningItemDto>
                {
                    new("Traffic is very bad. + We arrive late.", "Traffic was very bad, so we arrived late."),
                },
                stuck: new List<StuckWordDto>
                {
                    new("ข้าวต้ม", "rice porridge / congee"),
                    new("ห้าง", "shopping mall"),
                }),
            CancellationToken.None);

        var saved = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        saved.SentenceCombiningItemsJson.Should().Contain("Traffic was very bad");
        // Codepoint-exact Thai, NOT \u0E02-escaped.
        saved.StuckWordsJson.Should().Contain("ข้าวต้ม");
        saved.StuckWordsJson.Should().Contain("ห้าง");
        saved.StuckWordsJson.Should().NotContain("\\u0E");
        saved.StuckWordsJson.Should().NotContain("\\u0e");
    }

    [Fact]
    public async Task Accepts_a_thai_only_entry_with_zero_hits_and_zero_misses()
    {
        // The only real prod entry is Thai-only: no instance of an English rule
        // exists to hit or miss, and there are no English sentences to combine.
        using var fx = new HandlerTestFixture();
        var entry = WritingEntry.Create(
            fx.User.Id, new DateOnly(2026, 8, 16), "<p>[หนึ่ง สอง สาม passione]</p>", 41);
        fx.Db.WritingEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var dto = await Handler(fx).Handle(
            ACommand(entry.Id, hit: 0, miss: 0, items: new List<SentenceCombiningItemDto>()),
            CancellationToken.None);

        dto.CorrectedAt.Should().NotBeNull();
        var saved = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        saved.HitCount.Should().Be(0);
        saved.MissCount.Should().Be(0);
        saved.SentenceCombiningItemsJson.Should().Be("[]");
    }

    [Fact]
    public async Task A_second_correction_overwrites_the_first()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);
        var entry = await SeedPending(fx);
        await Handler(fx).Handle(ACommand(entry.Id, hit: 0, miss: 3), CancellationToken.None);

        fx.Clock.UtcNow = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
        await Handler(fx).Handle(ACommand(entry.Id, hit: 1, miss: 2), CancellationToken.None);

        var saved = fx.Db.WritingEntries.Single(w => w.Id == entry.Id);
        saved.CorrectedAt.Should().Be(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));
        saved.HitCount.Should().Be(1);
        saved.MissCount.Should().Be(2);
    }

    [Fact]
    public async Task Refuses_an_unknown_entry_id_with_the_standard_message()
    {
        using var fx = new HandlerTestFixture();

        var act = async () => await Handler(fx).Handle(
            ACommand(Guid.NewGuid()), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Writing entry not found.");
    }

    [Fact]
    public async Task Refuses_a_soft_deleted_entry()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);
        entry.SoftDelete();
        await fx.Db.SaveChangesAsync();

        var act = async () => await Handler(fx).Handle(ACommand(entry.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Writing entry not found.");
        fx.Db.WritingEntries.Single(w => w.Id == entry.Id).CorrectedAt.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_another_users_entry_with_the_same_not_found_message()
    {
        using var fx = new HandlerTestFixture();
        var other = User.CreateFromExternalLogin("other-oid", "other@example.com", "Other", AuthProvider.Microsoft);
        fx.Db.Users.Add(other);
        var theirs = WritingEntry.Create(other.Id, new DateOnly(2026, 8, 16), "<p>not mine at all</p>", 420);
        fx.Db.WritingEntries.Add(theirs);
        await fx.Db.SaveChangesAsync();

        var act = async () => await Handler(fx).Handle(ACommand(theirs.Id), CancellationToken.None);

        // "not found", never "forbidden" — a forbidden would confirm the id exists.
        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Writing entry not found.");
        fx.Db.WritingEntries.Single(w => w.Id == theirs.Id).CorrectedAt.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_a_marked_text_over_50000_characters()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id, markedText: new string('x', 50_001)), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Accepts_a_marked_text_of_exactly_50000_characters()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id, markedText: new string('x', 50_000)), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Rejects_more_than_four_sentence_combining_items()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);
        var five = Enumerable.Range(1, 5)
            .Select(i => new SentenceCombiningItemDto($"A{i}. + B{i}.", $"A{i} and B{i}."))
            .ToList();

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id, items: five), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Rejects_negative_counts()
    {
        using var fx = new HandlerTestFixture();
        var entry = await SeedPending(fx);

        var act = async () => await Handler(fx).Handle(
            ACommand(entry.Id, miss: -1), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~RecordWritingCorrectionHandlerTests
```
Expected: FAIL to compile — command/handler/validator/DTOs missing.

- [ ] **Step 3: Add the two block DTOs**

Append to `backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs`:

```csharp
/// <summary>
/// One sentence-combining item of the correction's block 3 — the writer's own
/// two short sentences and the combined version. Shape taken from the approved
/// mock (screens/writing-practice-critique-loop.html, frame 2 block 3):
/// "Traffic is very bad. + We arrive late." → "Traffic was very bad, so we arrived late."
/// </summary>
public sealed record SentenceCombiningItemDto(string Source, string Combined);

/// <summary>
/// One bracketed Thai word the writer got stuck on, with its English
/// translation — block 4 of the correction ("ข้าวต้ม → rice porridge / congee").
/// </summary>
public sealed record StuckWordDto(string Thai, string English);
```

- [ ] **Step 4: Create the command**

`…/RecordWritingCorrection/RecordWritingCorrectionCommand.cs`:

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Writing.RecordWritingCorrection;

/// <summary>
/// The one combined call carrying everything the critique loop produces for a
/// single entry (mcp-tool-contract's record_writing_correction). Marks the
/// entry corrected.
///
/// WordsPerMinute and target-errors-per-100-words are deliberately NOT inputs:
/// MenuNest already has elapsedSeconds and the text, and derives both numbers
/// itself from the hit/miss counts here plus the word count. Adding either as
/// an argument would move the computation into the AI's hands.
/// </summary>
public sealed record RecordWritingCorrectionCommand(
    Guid EntryId,
    string TargetRule,
    string MarkedText,
    int HitCount,
    int MissCount,
    string ThaiWhyLine,
    IReadOnlyList<SentenceCombiningItemDto> SentenceCombiningItems,
    IReadOnlyList<StuckWordDto> StuckWords) : ICommand<WritingEntryDto>;
```

- [ ] **Step 5: Create the validator**

`…/RecordWritingCorrection/RecordWritingCorrectionValidator.cs`:

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.RecordWritingCorrection;

public sealed class RecordWritingCorrectionValidator : AbstractValidator<RecordWritingCorrectionCommand>
{
    public RecordWritingCorrectionValidator()
    {
        RuleFor(x => x.EntryId).NotEmpty();

        RuleFor(x => x.TargetRule).NotEmpty()
            .MaximumLength(200).WithMessage("TargetRule must be 200 characters or less.");

        // Same ceiling as the entry Text it annotates (and markedText is always
        // longer than that text).
        RuleFor(x => x.MarkedText).NotEmpty()
            .MaximumLength(50_000).WithMessage("MarkedText must be 50,000 characters or less.");

        RuleFor(x => x.ThaiWhyLine).NotEmpty()
            .MaximumLength(2000).WithMessage("ThaiWhyLine must be 2,000 characters or less.");

        RuleFor(x => x.HitCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MissCount).GreaterThanOrEqualTo(0);

        // The contract asks for 3-4 items, but the minimum is NOT enforced: a
        // Thai-only night has no English sentences to combine (and the sole real
        // prod entry is exactly that). Only the upper bound is a rule.
        RuleFor(x => x.SentenceCombiningItems).NotNull()
            .Must(items => items.Count <= 4)
            .WithMessage("SentenceCombiningItems must contain 4 items or fewer.");
        RuleForEach(x => x.SentenceCombiningItems).ChildRules(item =>
        {
            item.RuleFor(i => i.Source).NotEmpty().MaximumLength(1000);
            item.RuleFor(i => i.Combined).NotEmpty().MaximumLength(1000);
        });

        RuleFor(x => x.StuckWords).NotNull()
            .Must(words => words.Count <= 50)
            .WithMessage("StuckWords must contain 50 items or fewer.");
        RuleForEach(x => x.StuckWords).ChildRules(word =>
        {
            word.RuleFor(w => w.Thai).NotEmpty().MaximumLength(200);
            word.RuleFor(w => w.English).NotEmpty().MaximumLength(200);
        });
    }
}
```

- [ ] **Step 6: Create the handler**

`…/RecordWritingCorrection/RecordWritingCorrectionHandler.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.RecordWritingCorrection;

public sealed class RecordWritingCorrectionHandler
    : ICommandHandler<RecordWritingCorrectionCommand, WritingEntryDto>
{
    /// <summary>
    /// Thai must land in the column as real characters. The default encoder
    /// escapes every non-ASCII codepoint to \uXXXX — valid JSON, but it
    /// bloats the column and makes the stored data unreadable.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<RecordWritingCorrectionCommand> _validator;
    private readonly IClock _clock;

    public RecordWritingCorrectionHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<RecordWritingCorrectionCommand> validator,
        IClock clock)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
        _clock = clock;
    }

    public async ValueTask<WritingEntryDto> Handle(
        RecordWritingCorrectionCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        // Same guard and same message as every other writing handler — "not
        // found" for a missing, deleted, or foreign entry alike, so the message
        // never confirms that someone else's id exists.
        var entry = await _db.WritingEntries
            .FirstOrDefaultAsync(w => w.Id == command.EntryId && w.UserId == user.Id && w.DeletedAt == null, ct)
            ?? throw new DomainException("Writing entry not found.");

        entry.RecordCorrection(
            correctedAtUtc: _clock.UtcNow,
            targetRule: command.TargetRule,
            markedText: command.MarkedText,
            hitCount: command.HitCount,
            missCount: command.MissCount,
            thaiWhyLine: command.ThaiWhyLine,
            sentenceCombiningItemsJson: JsonSerializer.Serialize(command.SentenceCombiningItems, JsonOptions),
            stuckWordsJson: JsonSerializer.Serialize(command.StuckWords, JsonOptions));

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

- [ ] **Step 7: Run the tests to verify they pass**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~RecordWritingCorrectionHandlerTests
```
Expected: PASS, 11 tests.

- [ ] **Step 8: Run the full backend suite, then commit**

```bash
cd backend && dotnet test
cd "c:/Repo2/t/menunest" && git add \
  backend/src/MenuNest.Application/UseCases/Writing/RecordWritingCorrection/ \
  backend/src/MenuNest.Application/UseCases/Writing/WritingDtos.cs \
  backend/tests/MenuNest.Application.UnitTests/Writing/RecordWritingCorrectionHandlerTests.cs
git commit -m "feat(writing): RecordWritingCorrection use case with the 5 correction blocks (#97)"
```

---

## Task 6: The `WritingTools` MCP class

Covers WMCP-01 (discovery), WMCP-12 (closed tool set), WMCP-16 (error mapping), WMCP-32 (descriptions).

**Files:**
- Create: `backend/src/MenuNest.McpServer/Tools/WritingTools.cs`
- Modify: `backend/src/MenuNest.McpServer/McpServerRegistration.cs:17`
- Test: `backend/tests/MenuNest.McpServer.UnitTests/Tools/WritingToolsTests.cs`

**Interfaces:**
- Consumes: `GetActiveTargetRuleQuery`, `SetActiveTargetRuleCommand` (Task 3); `ListPendingWritingEntriesQuery` (Task 4); `RecordWritingCorrectionCommand` (Task 5).
- Produces: the 4 MCP tools. Nothing else depends on this class.

> `MenuNest.McpServer/GlobalUsings.cs` already provides `System.ComponentModel`, `Mediator` and the MCP server namespaces — `TripTools.cs` uses `[Description]` and `IMediator` with no local `using` for either. Follow that; do not add redundant usings.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/MenuNest.McpServer.UnitTests/Tools/WritingToolsTests.cs`:

```csharp
using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.GetActiveTargetRule;
using MenuNest.Application.UseCases.Writing.ListPendingWritingEntries;
using MenuNest.Application.UseCases.Writing.RecordWritingCorrection;
using MenuNest.Application.UseCases.Writing.SetActiveTargetRule;
using MenuNest.McpServer.Tools;
using ModelContextProtocol.Server;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class WritingToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly WritingTools _sut;

    public WritingToolsTests() => _sut = new WritingTools(_mediator.Object);

    [Fact]
    public async Task list_pending_writing_entries_sends_the_query()
    {
        IReadOnlyList<PendingWritingEntryDto> expected = new List<PendingWritingEntryDto>
        {
            new(Guid.NewGuid(), new DateOnly(2026, 8, 16), "<p>pending night</p>", 41, 5.853658536585366),
        };
        _mediator
            .Setup(m => m.Send(It.IsAny<ListPendingWritingEntriesQuery>(), It.IsAny<CancellationToken>()))
            .Returns<ListPendingWritingEntriesQuery, CancellationToken>(
                (_, _) => new ValueTask<IReadOnlyList<PendingWritingEntryDto>>(expected));

        var result = await _sut.list_pending_writing_entries(CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.IsAny<ListPendingWritingEntriesQuery>(), It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task get_active_target_rule_sends_the_query()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<GetActiveTargetRuleQuery>(), It.IsAny<CancellationToken>()))
            .Returns<GetActiveTargetRuleQuery, CancellationToken>(
                (_, _) => new ValueTask<string?>("third-person singular -s"));

        var result = await _sut.get_active_target_rule(CancellationToken.None);

        result.Should().Be("third-person singular -s");
    }

    [Fact]
    public async Task get_active_target_rule_passes_through_a_null_unset_rule()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<GetActiveTargetRuleQuery>(), It.IsAny<CancellationToken>()))
            .Returns<GetActiveTargetRuleQuery, CancellationToken>((_, _) => new ValueTask<string?>((string?)null));

        var result = await _sut.get_active_target_rule(CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task set_active_target_rule_sends_the_command_with_the_rule()
    {
        _mediator
            .Setup(m => m.Send(
                It.Is<SetActiveTargetRuleCommand>(c => c.Rule == "articles (a/an/the)"),
                It.IsAny<CancellationToken>()))
            .Returns<SetActiveTargetRuleCommand, CancellationToken>(
                (_, _) => new ValueTask<string?>("articles (a/an/the)"));

        var result = await _sut.set_active_target_rule("articles (a/an/the)", CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<SetActiveTargetRuleCommand>(c => c.Rule == "articles (a/an/the)"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().Be("articles (a/an/the)");
    }

    [Fact]
    public async Task record_writing_correction_sends_every_block_on_the_command()
    {
        var entryId = Guid.NewGuid();
        var expected = new WritingEntryDto(
            entryId, new DateOnly(2026, 8, 16), "<p>She go to school.</p>", 420, 8.1,
            new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc), new DateTime(2026, 8, 16, 13, 46, 59, DateTimeKind.Utc));
        _mediator
            .Setup(m => m.Send(It.IsAny<RecordWritingCorrectionCommand>(), It.IsAny<CancellationToken>()))
            .Returns<RecordWritingCorrectionCommand, CancellationToken>((_, _) => new ValueTask<WritingEntryDto>(expected));

        var result = await _sut.record_writing_correction(
            entryId: entryId,
            targetRule: "third-person singular -s",
            markedText: "<p>She <span class=\"miss\">go</span> <span class=\"fix\">→ goes</span> to school.</p>",
            hitCount: 0,
            missCount: 1,
            thaiWhyLine: "ประธานเป็น he / she / it → กริยาต้องเติม -s",
            sentenceCombiningItems: new List<SentenceCombiningItemDto>
            {
                new("Traffic is very bad. + We arrive late.", "Traffic was very bad, so we arrived late."),
            },
            stuckWords: new List<StuckWordDto> { new("ข้าวต้ม", "rice porridge / congee") },
            ct: CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<RecordWritingCorrectionCommand>(c =>
                c.EntryId == entryId &&
                c.TargetRule == "third-person singular -s" &&
                c.HitCount == 0 &&
                c.MissCount == 1 &&
                c.MarkedText.Contains("→ goes") &&
                c.ThaiWhyLine.Contains("เติม -s") &&
                c.SentenceCombiningItems.Count == 1 &&
                c.StuckWords.Count == 1 &&
                c.StuckWords[0].Thai == "ข้าวต้ม"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void Exposes_exactly_the_four_contracted_tools_and_no_create_or_edit_tool()
    {
        var toolNames = typeof(WritingTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => m.Name)
            .ToList();

        toolNames.Should().BeEquivalentTo(new[]
        {
            "list_pending_writing_entries",
            "get_active_target_rule",
            "set_active_target_rule",
            "record_writing_correction",
        });
        // Entry creation and text editing stay in-app, never MCP
        // (mcp-tool-contract.md:38).
        toolNames.Should().NotContain(n =>
            n.Contains("submit") || n.Contains("create") || n.Contains("update_writing") || n.Contains("delete"));
    }

    [Fact]
    public void Every_tool_and_every_parameter_carries_a_description()
    {
        var tools = typeof(WritingTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

        tools.Should().HaveCount(4);
        foreach (var tool in tools)
        {
            tool.GetCustomAttribute<DescriptionAttribute>()
                .Should().NotBeNull($"{tool.Name} needs a [Description] so Claude Code knows when to call it");

            foreach (var p in tool.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken)))
            {
                p.GetCustomAttribute<DescriptionAttribute>()
                    .Should().NotBeNull($"{tool.Name}.{p.Name} needs a [Description]");
            }
        }
    }

    [Fact]
    public void record_writing_correction_takes_no_derived_number_arguments()
    {
        // The contract is explicit: MenuNest computes words-per-minute and
        // target-errors-per-100-words itself. Accepting either as an argument
        // would move the computation into the AI's hands.
        var parameters = typeof(WritingTools)
            .GetMethod(nameof(WritingTools.record_writing_correction))!
            .GetParameters()
            .Select(p => p.Name!.ToLowerInvariant())
            .ToList();

        parameters.Should().NotContain(p => p.Contains("wordsperminute") || p.Contains("wpm"));
        parameters.Should().NotContain(p => p.Contains("per100") || p.Contains("errorrate"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd backend && dotnet test tests/MenuNest.McpServer.UnitTests
```
Expected: FAIL to compile — `WritingTools` does not exist.

- [ ] **Step 3: Create the tool class**

`backend/src/MenuNest.McpServer/Tools/WritingTools.cs`:

```csharp
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.GetActiveTargetRule;
using MenuNest.Application.UseCases.Writing.ListPendingWritingEntries;
using MenuNest.Application.UseCases.Writing.RecordWritingCorrection;
using MenuNest.Application.UseCases.Writing.SetActiveTargetRule;

namespace MenuNest.McpServer.Tools;

/// <summary>
/// The writing-practice correction loop over MCP (issue #97 Phase 2,
/// ai-correction-invocation Path B). Four tools, and deliberately no fifth:
/// creating or editing an entry stays in the MenuNest page's own submit button,
/// never MCP (mcp-tool-contract).
/// </summary>
[McpServerToolType]
public sealed class WritingTools(IMediator mediator)
{
    [McpServerTool, Description(
        "List the writer's freewrite nights that have NO correction yet, newest first. Call this FIRST to find what needs correcting — the writer does not have to name a date. Returns id, date, text (RTE HTML, with Thai stuck-words in [square brackets]), elapsedSeconds and wordsPerMinute. An empty list means every night is already corrected.")]
    public async Task<IReadOnlyList<PendingWritingEntryDto>> list_pending_writing_entries(CancellationToken ct)
        => await mediator.Send(new ListPendingWritingEntriesQuery(), ct);

    [McpServerTool, Description(
        "Get the ONE grammar rule to grade tonight's writing against, e.g. 'third-person singular -s'. Returns null when the writer has never set one — in that case ASK them which rule they want this month and call set_active_target_rule before correcting anything. Never guess a rule.")]
    public async Task<string?> get_active_target_rule(CancellationToken ct)
        => await mediator.Send(new GetActiveTargetRuleQuery(), ct);

    [McpServerTool, Description(
        "Change the active target grammar rule. The writer normally flips this on MenuNest's settings screen; this tool is the same underlying value, for when they ask in chat instead ('change my rule to articles'). Pass an empty string to clear it back to unset. Returns the stored rule.")]
    public async Task<string?> set_active_target_rule(
        [Description("The new target grammar rule, max 200 chars, e.g. 'articles (a/an/the)'. Empty clears it.")] string rule,
        CancellationToken ct)
        => await mediator.Send(new SetActiveTargetRuleCommand(rule), ct);

    [McpServerTool, Description(
        "Record the complete 5-block correction for ONE night and mark it corrected. Grade against the ONE active target rule only — mark its instances in place and leave every other error (articles, tense, spelling) untouched and unmentioned. Never rewrite the writer's text, never score or praise. Re-calling this on an already-corrected night OVERWRITES the previous correction, which is how a bad pass is repaired. Do NOT pass words-per-minute or errors-per-100-words: MenuNest computes both itself.")]
    public async Task<WritingEntryDto> record_writing_correction(
        [Description("The entry id from list_pending_writing_entries")] Guid entryId,
        [Description("The rule this correction graded against — normally the value get_active_target_rule returned")] string targetRule,
        [Description("The writer's ORIGINAL text with only this rule's instances marked in place. A miss: <span class=\"miss\">go</span> <span class=\"fix\">→ goes</span>. A hit: <span class=\"hit\">is</span>. Keep the writer's [Thai brackets] as <span class=\"th\">[ข้าวต้ม]</span>. Copy every other word through verbatim. Max 50,000 chars.")] string markedText,
        [Description("How many instances of the target rule the writer got RIGHT, counted mechanically. 0 is valid (e.g. a Thai-only night has no instances at all).")] int hitCount,
        [Description("How many instances of the target rule the writer got WRONG, counted mechanically. 0 is valid.")] int missCount,
        [Description("ONE line in Thai explaining why the rule holds — the mechanism, not a translation. Max 2,000 chars.")] string thaiWhyLine,
        [Description("0-4 sentence-combining items built from the writer's OWN sentences that night. Each carries source ('Traffic is very bad. + We arrive late.') and combined ('Traffic was very bad, so we arrived late.'). Send an empty list when the night has no English sentences to combine.")] IReadOnlyList<SentenceCombiningItemDto> sentenceCombiningItems,
        [Description("The Thai words the writer bracketed because he could not produce them in English, each with its English translation, e.g. thai 'ข้าวต้ม' / english 'rice porridge / congee'. Empty list when there were none.")] IReadOnlyList<StuckWordDto> stuckWords,
        CancellationToken ct)
        => await mediator.Send(
            new RecordWritingCorrectionCommand(
                entryId, targetRule, markedText, hitCount, missCount,
                thaiWhyLine, sentenceCombiningItems, stuckWords),
            ct);
}
```

- [ ] **Step 4: Register the tool class**

In `backend/src/MenuNest.McpServer/McpServerRegistration.cs`, after the `TripTools` line:

```csharp
            .WithTools<Tools.TripTools>()
            .WithTools<Tools.WritingTools>()
```

- [ ] **Step 5: Run the MCP tests to verify they pass**

```bash
cd backend && dotnet test tests/MenuNest.McpServer.UnitTests
```
Expected: PASS, all tests including the 4-tool reflection assertions.

- [ ] **Step 6: Run the full backend suite, then commit**

```bash
cd backend && dotnet test
cd "c:/Repo2/t/menunest" && git add \
  backend/src/MenuNest.McpServer/Tools/WritingTools.cs \
  backend/src/MenuNest.McpServer/McpServerRegistration.cs \
  backend/tests/MenuNest.McpServer.UnitTests/Tools/WritingToolsTests.cs
git commit -m "feat(writing): expose the 4 WritingTools MCP tools (#97)"
```

- [ ] **Step 7: Apply BOTH migrations to prod by hand**

The backend is now complete and about to be deployed by the push. Apply the migrations **before** pushing, or `/writing` and `/api/me` return HTTP 500 (`Invalid column name 'ActiveTargetRule'`).

```bash
# 1. Confirm the personal az session
az account show --query "{name:name, user:user.name}" -o json   # expect Pay-As-You-Go / thodsaphonSP@hotmail.co.th

# 2. Preview the SQL first
cd backend && dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi

# 3. Open the firewall for this machine only, temporarily
IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply \
  --start-ip-address $IP --end-ip-address $IP

# 4. Apply
cd backend && AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"

# 5. Close the firewall again — ALWAYS, even if step 4 failed
az sql server firewall-rule delete --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply
```

> If `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` is rejected with *"Valid values are 'dev' or 'prod'"*, the installed `Azure.Identity` no longer accepts that value. Fall back to `AZURE_TOKEN_CREDENTIALS=dev` (which still includes the Azure CLI credential) and verify `az account show` is the personal account first.

Expected: `Done.` Verify with `dotnet ef migrations list` that both `AddUserSettingsActiveTargetRule` and `AddWritingEntryMarkedText` show as applied.

---

## Task 7: Expose the rule over HTTP for the SPA

Covers WMCP-09 (both routes, one value).

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Me/MeDto.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs:30-40`
- Modify: `backend/src/MenuNest.WebApi/Controllers/MeController.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Me/GetMeHandlerTests.cs`

**Interfaces:**
- Consumes: `SetActiveTargetRuleCommand` (Task 3), `UserSettings.ActiveTargetRule` (Task 1).
- Produces: `MeDto.ActiveTargetRule` (`string?`, the 11th positional member, appended last) and `PUT /api/me/target-rule` taking `{ "rule": "…" }` and returning `{ "activeTargetRule": "…" }`. Tasks 8 and 9 consume both.

> `new MeDto(` has exactly ONE construction site (`GetMeHandler.cs:30`) and it uses named arguments — appending a member is safe. Verify with `grep -rn "new MeDto(" backend --include=*.cs | grep -v /obj/` before editing.

- [ ] **Step 1: Write the failing test**

Append to `backend/tests/MenuNest.Application.UnitTests/Me/GetMeHandlerTests.cs` (inside the existing class):

```csharp
    [Fact]
    public async Task Returns_the_active_target_rule_when_one_is_set()
    {
        using var fx = new HandlerTestFixture();
        var settings = UserSettings.Create(fx.User.Id);
        settings.SetActiveTargetRule("third-person singular -s");
        fx.Db.UserSettings.Add(settings);
        await fx.Db.SaveChangesAsync();
        var handler = new GetMeHandler(fx.UserProvisioner.Object, fx.Db);

        var me = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        me.ActiveTargetRule.Should().Be("third-person singular -s");
    }

    [Fact]
    public async Task Returns_a_null_active_target_rule_when_no_settings_row_exists()
    {
        using var fx = new HandlerTestFixture();
        var handler = new GetMeHandler(fx.UserProvisioner.Object, fx.Db);

        var me = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        me.ActiveTargetRule.Should().BeNull();
    }
```

Add `using MenuNest.Domain.Entities;` to the file's usings if it is not already there.

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~GetMeHandlerTests
```
Expected: FAIL to compile — `MeDto` has no `ActiveTargetRule`.

- [ ] **Step 3: Add the field to `MeDto` and `GetMeHandler`**

In `MeDto.cs`, append the member last and extend the doc comment:

```csharp
public sealed record MeDto(
    Guid UserId,
    string Email,
    string DisplayName,
    Guid? FamilyId,
    string? FamilyName,
    string? FamilyInviteCode,
    string AuthProvider,
    string? HomePath,
    int? UvWarnThreshold,
    int? FeelsLikeWarnThreshold,
    string? ActiveTargetRule);
```

In `GetMeHandler.Handle`, add the last argument:

```csharp
            FeelsLikeWarnThreshold: settings?.FeelsLikeWarnThreshold,
            ActiveTargetRule: settings?.ActiveTargetRule);
```

- [ ] **Step 4: Add the endpoint**

In `backend/src/MenuNest.WebApi/Controllers/MeController.cs`, add the using and the action, then the request/response records at the bottom of the file:

```csharp
using MenuNest.Application.UseCases.Writing.SetActiveTargetRule;
```

```csharp
    /// <summary>
    /// Sets the caller's active target grammar rule — the in-app half of
    /// mcp-tool-contract's set_active_target_rule (the MCP tool writes the same
    /// value). Deliberately separate from PUT settings, whose full-snapshot
    /// body does not carry the rule.
    /// </summary>
    [HttpPut("target-rule")]
    public async Task<ActionResult<ActiveTargetRuleResponse>> SetTargetRule(
        [FromBody] SetTargetRuleRequest request, CancellationToken ct)
    {
        var rule = await _mediator.Send(new SetActiveTargetRuleCommand(request.Rule), ct);
        return Ok(new ActiveTargetRuleResponse(rule));
    }
```

```csharp
public sealed record SetTargetRuleRequest(string? Rule);

public sealed record ActiveTargetRuleResponse(string? ActiveTargetRule);
```

- [ ] **Step 5: Run the tests, then the full suite**

```bash
cd backend && dotnet test tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~GetMeHandlerTests
cd backend && dotnet test
```
Expected: both PASS, 0 failures.

- [ ] **Step 6: Commit**

```bash
cd "c:/Repo2/t/menunest" && git add \
  backend/src/MenuNest.Application/UseCases/Me/MeDto.cs \
  backend/src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs \
  backend/src/MenuNest.WebApi/Controllers/MeController.cs \
  backend/tests/MenuNest.Application.UnitTests/Me/GetMeHandlerTests.cs
git commit -m "feat(writing): expose the active target rule on /api/me and PUT /api/me/target-rule (#97)"
```

---

## Task 8: The in-app rule control (Path 1)

Covers WMCP-09. Path 1 is the writer's everyday route and must exist, or only the MCP path can flip the rule.

**Files:**
- Create: `frontend/src/pages/settings/targetRuleOptions.ts`
- Create: `frontend/src/pages/settings/targetRuleOptions.test.ts`
- Modify: `frontend/src/shared/api/api.ts:133` (MeDto type) and the endpoints block
- Modify: `frontend/src/shared/hooks/useCurrentUser.ts:62`
- Modify: `frontend/src/pages/settings/SettingsPage.tsx`, `frontend/src/pages/settings/SettingsPage.css`
- Create: `frontend/e2e/writing.target-rule.spec.ts`

**Interfaces:**
- Consumes: `MeDto.activeTargetRule`, `PUT /api/me/target-rule` (Task 7).
- Produces: `useSetActiveTargetRuleMutation()` from `shared/api/api`, and `TARGET_RULE_PRESETS` from `pages/settings/targetRuleOptions`.

- [ ] **Step 1: Write the failing unit test for the pure helper**

Create `frontend/src/pages/settings/targetRuleOptions.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { TARGET_RULE_PRESETS, normalizeTargetRule, MAX_TARGET_RULE_LENGTH } from './targetRuleOptions'

describe('targetRuleOptions', () => {
  it('offers the presets the correction loop is written around', () => {
    expect(TARGET_RULE_PRESETS).toContain('third-person singular -s')
    expect(TARGET_RULE_PRESETS).toContain('articles (a/an/the)')
    expect(TARGET_RULE_PRESETS.length).toBeGreaterThanOrEqual(3)
  })

  it('trims a rule', () => {
    expect(normalizeTargetRule('  plural -s  ')).toBe('plural -s')
  })

  it('turns a blank rule into null so the server clears it', () => {
    expect(normalizeTargetRule('')).toBeNull()
    expect(normalizeTargetRule('   ')).toBeNull()
    expect(normalizeTargetRule(null)).toBeNull()
  })

  it('truncates at the 200-char server ceiling instead of sending a rejected value', () => {
    const long = 'x'.repeat(250)

    const result = normalizeTargetRule(long)

    expect(result).not.toBeNull()
    expect(result!.length).toBe(MAX_TARGET_RULE_LENGTH)
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

```bash
cd frontend && npx vitest run src/pages/settings/targetRuleOptions.test.ts
```
Expected: FAIL — cannot resolve `./targetRuleOptions`.

- [ ] **Step 3: Write the helper**

Create `frontend/src/pages/settings/targetRuleOptions.ts`:

```ts
// The active target grammar rule the AI correction loop grades against.
// The writer flips it by hand — there is no calendar rotation (rule-rotation).
// Kept as free text with presets rather than an enum: the rule is a teaching
// choice, not a system value, and the server only bounds its length.

/** Matches UserSettings.ActiveTargetRule / WritingEntries.TargetRule nvarchar(200). */
export const MAX_TARGET_RULE_LENGTH = 200

export const TARGET_RULE_PRESETS = [
  'third-person singular -s',
  'articles (a/an/the)',
  'past simple -ed',
  'plural -s',
] as const

/**
 * Trims, collapses a blank rule to null (which clears it server-side), and
 * caps at the column ceiling so the PUT cannot be rejected for length.
 */
export function normalizeTargetRule(rule: string | null | undefined): string | null {
  const trimmed = (rule ?? '').trim()
  if (trimmed.length === 0) return null
  return trimmed.slice(0, MAX_TARGET_RULE_LENGTH)
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
cd frontend && npx vitest run src/pages/settings/targetRuleOptions.test.ts
```
Expected: PASS, 4 tests.

- [ ] **Step 5: Add the API surface**

In `frontend/src/shared/api/api.ts`, add the field to the `MeDto` interface (after `feelsLikeWarnThreshold`):

```ts
    feelsLikeWarnThreshold: number | null
    activeTargetRule: string | null
```

and add this endpoint next to `updateUserSettings`:

```ts
        // The in-app half of mcp-tool-contract's set_active_target_rule. Separate
        // from updateUserSettings because that PUT is a full snapshot (ADR-091)
        // and does not carry the rule.
        setActiveTargetRule: build.mutation<{ activeTargetRule: string | null }, { rule: string | null }>({
            query: (body) => ({
                url: '/api/me/target-rule',
                method: 'PUT',
                body,
            }),
            invalidatesTags: ['Me'],
            async onQueryStarted(arg, {dispatch, queryFulfilled}) {
                const patch = dispatch(
                    api.util.updateQueryData('getMe', undefined, (draft) => {
                        draft.activeTargetRule = arg.rule
                    })
                )
                try {
                    await queryFulfilled
                } catch {
                    patch.undo()
                }
            },
        }),
```

Then export the hook alongside `useUpdateUserSettingsMutation` in the destructured export block near `api.ts:1668`:

```ts
    useSetActiveTargetRuleMutation,
```

- [ ] **Step 6: Surface it on the profile hook**

In `frontend/src/shared/hooks/useCurrentUser.ts`, beside `homePath`:

```ts
    activeTargetRule: me?.activeTargetRule ?? null,
```

- [ ] **Step 7: Add the control to the settings screen**

In `frontend/src/pages/settings/SettingsPage.tsx`: pull `activeTargetRule` from `useCurrentUser()`, add `const [setTargetRule, { isLoading: isSavingRule }] = useSetActiveTargetRuleMutation()`, and render a section styled like the existing ones. Use the repo's Syncfusion `DropDownList` with `allowFiltering`-style free entry if it is already used that way elsewhere in this file; otherwise a plain `<input>` plus a preset row is acceptable — do not introduce a new Syncfusion package.

```tsx
      <section className="settings-section">
        <h2 className="settings-section__title">กฎเป้าหมายเดือนนี้</h2>
        <p className="settings-section__hint">
          กฎเดียวที่ AI จะตรวจให้ — ที่ผิดข้ออื่นจะเห็นแต่เงียบไว้. เปลี่ยนที่นี่ หรือบอก Claude ในแชทก็ได้ (ค่าเดียวกัน)
        </p>
        <input
          className="settings-rule-input"
          type="text"
          value={ruleDraft}
          maxLength={MAX_TARGET_RULE_LENGTH}
          placeholder="ยังไม่ได้ตั้ง — AI จะถามก่อนตรวจ"
          onChange={(e) => setRuleDraft(e.target.value)}
          onBlur={() => void persistRule()}
        />
        <div className="settings-rule-presets">
          {TARGET_RULE_PRESETS.map((preset) => (
            <button
              key={preset}
              type="button"
              className="settings-rule-preset"
              onClick={() => { setRuleDraft(preset); void persistRule(preset) }}
            >
              {preset}
            </button>
          ))}
        </div>
        {isSavingRule && <span className="settings-saved">กำลังบันทึก...</span>}
      </section>
```

with, alongside the other state in the component:

```tsx
  const [ruleDraft, setRuleDraft] = useState('')

  // Hydrate once, same pattern and same reason as the weather thresholds above:
  // saving patches the getMe cache, so re-syncing on later changes would fight
  // the user's typing.
  const hasHydratedRule = useRef(false)
  useEffect(() => {
    if (isLoadingProfile || hasHydratedRule.current) return
    hasHydratedRule.current = true
    setRuleDraft(activeTargetRule ?? '')
  }, [isLoadingProfile, activeTargetRule])

  const persistRule = async (explicit?: string) => {
    if (isLoadingProfile) return
    const rule = normalizeTargetRule(explicit ?? ruleDraft)
    if (rule === (activeTargetRule ?? null)) return
    try {
      await setTargetRule({ rule }).unwrap()
    } catch (err) {
      console.error('setActiveTargetRule failed', err)
    }
  }
```

Add matching styles to `SettingsPage.css` following the file's existing token/class conventions (reuse the section, hint and button styles already there — do not invent a new visual language).

- [ ] **Step 8: Add the e2e spec**

Create `frontend/e2e/writing.target-rule.spec.ts`, modelled on an existing settings spec (`frontend/e2e/health.settings.spec.ts` is the closest). Mock `GET /api/me` to return `activeTargetRule: null`, assert the placeholder renders, click the `third-person singular -s` preset, and assert a `PUT /api/me/target-rule` fires with `{"rule":"third-person singular -s"}`.

- [ ] **Step 9: Run the gates**

```bash
cd frontend && npx tsc --noEmit && npm run build && npx vitest run
cd frontend && npx playwright test e2e/writing.target-rule.spec.ts
```
Expected: all green.

- [ ] **Step 10: Verify interactively, then commit**

Run the app, open `/settings`, confirm the section renders correctly on a phone-width viewport (the writer uses this one-handed), set a rule, reload, and confirm it persists. Then:

```bash
cd "c:/Repo2/t/menunest" && git add \
  frontend/src/pages/settings/targetRuleOptions.ts \
  frontend/src/pages/settings/targetRuleOptions.test.ts \
  frontend/src/pages/settings/SettingsPage.tsx \
  frontend/src/pages/settings/SettingsPage.css \
  frontend/src/shared/api/api.ts \
  frontend/src/shared/hooks/useCurrentUser.ts \
  frontend/e2e/writing.target-rule.spec.ts
git commit -m "feat(writing): in-app control for the active target rule (#97)"
```

---

## Task 9: Live lock on the detail page (the mid-edit defect)

Covers WMCP-26 (hard gate), WMCP-07. This defect is *armed* by Task 6 — before it, nothing could set `CorrectedAt`.

**Files:**
- Modify: `frontend/src/pages/writing/WritingEntryDetailPage.tsx:33,35-46`
- Modify: `frontend/src/pages/writing/WritingEntryDetailPage.css`
- Modify: `frontend/src/shared/api/api.ts` (the `listWritingEntries` query — add polling)
- Create: `frontend/e2e/writing.live-lock.spec.ts`

**Interfaces:**
- Consumes: `correctedAt` on `WritingEntryDto` (already shipped).
- Produces: nothing other tasks depend on.

**Behaviour to build** (writer's decision, 2026-08-17 — *live lock*):
1. While the detail page is open, `listWritingEntries` polls every 15s so `correctedAt` arriving over MCP is noticed without a manual reload.
2. The moment `correctedAt` becomes non-null, the page leaves edit mode, shows the existing locked note, and the Save button is gone. Typed-but-unsaved text is discarded — accepted.
3. A save that still races the lock and gets refused no longer says *"try again"*. It says the night was just corrected and cannot be edited.

- [ ] **Step 1: Write the failing e2e spec**

Create `frontend/e2e/writing.live-lock.spec.ts`, following `frontend/e2e/writing.history.spec.ts`'s fixture and route-mocking style:

```ts
import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'

// The mid-edit lock defect (WMCP-26). A correction landing over MCP while the
// writer is editing used to leave Save enabled, fail the PUT, and show a
// "try again" message for something that can never succeed.
const ENTRY_ID = '22222222-2222-2222-2222-222222222222'

const pending = {
  id: ENTRY_ID,
  date: '2026-08-16',
  text: '<p>Pending entry text.</p>',
  elapsedSeconds: 420,
  wordsPerMinute: 28,
  correctedAt: null,
  createdAt: '2026-08-16T09:00:00Z',
}
const corrected = { ...pending, correctedAt: '2026-08-17T02:00:00Z' }

test.describe('Writing — live lock while editing', () => {
  test('locks the editor and drops Save when a correction lands mid-edit', async ({ page }) => {
    let hasBeenCorrected = false
    await page.route('**/api/writing-entries', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([hasBeenCorrected ? corrected : pending]),
      })
    })

    await page.goto(`/writing/history/${ENTRY_ID}`)
    await page.getByRole('button', { name: /แก้ไข/ }).click()
    await expect(page.getByRole('button', { name: /บันทึก/ })).toBeVisible()

    // The correction lands over MCP; the page must notice on its own.
    hasBeenCorrected = true

    await expect(page.getByText('ตรวจแล้ว — แก้ข้อความไม่ได้ (ลบทั้งรายการได้)')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('button', { name: /^บันทึก$/ })).toHaveCount(0)
    // Delete stays available even when locked (ADR-169).
    await expect(page.getByRole('button', { name: /ลบ/ })).toBeVisible()
  })
})
```

> Confirm the real route path and the Thai button labels against `WritingEntryDetailPage.tsx` and `router.tsx` before running — use the labels the component actually renders, not these guesses, and update this spec accordingly. The assertions (locked note visible, Save gone, Delete present) are the contract; the selectors are not.

- [ ] **Step 2: Run it to verify it fails**

```bash
cd frontend && npx playwright test e2e/writing.live-lock.spec.ts
```
Expected: FAIL — Save is still present after the correction lands.

- [ ] **Step 3: Poll while a detail page is open**

In `frontend/src/shared/api/api.ts`, the `listWritingEntries` query feeds both the History list and the detail page. Rather than polling globally, opt in from the component. In `WritingEntryDetailPage.tsx`, change the existing query call to:

```tsx
  // Poll while this page is open: a correction can only arrive over MCP (from
  // the writer's Claude Code), so without polling the page keeps a stale
  // correctedAt and offers an edit the server will refuse (WMCP-26).
  const { data: entries, isLoading, isError } = useListWritingEntriesQuery(undefined, {
    pollingInterval: 15_000,
  })
```

- [ ] **Step 4: Leave edit mode the moment the lock appears**

In `WritingEntryDetailPage.tsx`, after `const isLocked = Boolean(entry?.correctedAt)`:

```tsx
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
```

- [ ] **Step 5: Make the refused-save message honest**

Replace the `catch` in `handleSave`:

```tsx
    } catch (err) {
      console.error('updateWritingEntryText failed', err)
      // A correction may have landed between render and save. "Try again" would
      // be a lie — that PUT can never succeed.
      setError(
        isLocked
          ? 'คืนนี้ถูกตรวจแล้ว — แก้ข้อความไม่ได้'
          : 'บันทึกไม่สำเร็จ ลองอีกครั้ง'
      )
    }
```

Also guard the Save button so it cannot be clicked once locked — at `WritingEntryDetailPage.tsx:128`:

```tsx
            <button type="button" className="writing-detail-save-btn" onClick={handleSave} disabled={isSaving || isLocked}>
```

- [ ] **Step 6: Run the spec to verify it passes**

```bash
cd frontend && npx playwright test e2e/writing.live-lock.spec.ts
```
Expected: PASS.

- [ ] **Step 7: Run every frontend gate**

```bash
cd frontend && npx tsc --noEmit && npm run build && npx vitest run
cd frontend && npx playwright test
```
Expected: all green — including the pre-existing `writing.history` spec, which must not regress.

- [ ] **Step 8: Verify interactively against the real loop**

This is the hard gate. With the backend deployed and the migrations applied:
1. On the phone (or a phone-width browser), open a pending night's detail page and tap edit. Type something.
2. From Claude Code, call `record_writing_correction` on that same entry.
3. Without touching the phone, wait up to 15s.
4. Confirm the editor locks itself, the locked note appears, Save is gone, and Delete still works.
5. Confirm no "ลองอีกครั้ง" message appears anywhere in that flow.

- [ ] **Step 9: Commit**

```bash
cd "c:/Repo2/t/menunest" && git add \
  frontend/src/pages/writing/WritingEntryDetailPage.tsx \
  frontend/src/pages/writing/WritingEntryDetailPage.css \
  frontend/src/shared/api/api.ts \
  frontend/e2e/writing.live-lock.spec.ts
git commit -m "fix(writing): lock the detail editor live when a correction lands mid-edit (#97)"
```

---

## Test-suite coverage map

Every case in `docs/test-cases/writing-mcp-tools-test-cases.xlsx` has a home. **Hard gates in bold.**

| Case | Where it is satisfied |
|---|---|
| **WMCP-01** discovery | Task 6 (class + registration) · final walkthrough |
| WMCP-02 null rule when unset | Task 3 |
| **WMCP-03** pending list fields | Task 4 · final walkthrough |
| **WMCP-04** correction stored | Tasks 2, 5 |
| **WMCP-05** corrected leaves pending | Task 4 |
| WMCP-06 History badge flips | Final walkthrough (grid already shipped; `writing.history` e2e must not regress — Task 9 step 7) |
| WMCP-07 lock + still deletable | Task 2 · Task 9 e2e |
| WMCP-08 rule round-trip | Tasks 1, 3 |
| WMCP-09 both routes, one value | Task 3 test · Tasks 7, 8 |
| WMCP-10 unauth 401 | Final verification below — a pre-change baseline measured 2026-08-17, not new work |
| **WMCP-11** cross-user isolation | Tasks 4, 5 tests · final verification |
| **WMCP-12** closed tool set | Task 6 reflection test |
| WMCP-13 soft-deleted excluded | Task 4 |
| WMCP-14 correcting a deleted entry | Task 5 |
| WMCP-15 overwrite semantics | Tasks 2, 5 |
| WMCP-16 clean error, not a crash | Task 5 · the server-wide filter at `McpServerRegistration.cs:21-26` |
| WMCP-17 CreatedAt tie-break | Task 4 |
| WMCP-18 nbsp word count | Already shipped and tested (commit 441fe6a) — regression guarded by the full suite each commit |
| WMCP-19 markedText cap | Task 5 validator |
| WMCP-20 elapsed 1..3600 | Already shipped (`SubmitWritingEntryValidator.cs:12-16`) — no MCP input path exists (WMCP-12) |
| WMCP-21 rule 200-char cap | Tasks 1, 3 |
| WMCP-22 blank rule clears | Tasks 1, 3 |
| WMCP-23 WPM untouched | Task 2 |
| WMCP-24 derived numbers not inputs | Task 6 test (the in-scope half); the numbers themselves ship with the progress screen |
| WMCP-25 Thai-only 0/0 | Task 5 · final walkthrough on the real 2026-08-16 night |
| **WMCP-26** mid-edit live lock | Task 9 |
| WMCP-27 Thai JSON un-escaped | Task 5 (`UnsafeRelaxedJsonEscaping`) |
| WMCP-28 0-4 combining items | Task 5 validator |
| WMCP-29 marks only the target rule | Task 6 tool `[Description]` · verified by reading a real correction in the walkthrough |
| WMCP-30 no streak anywhere | Deferred with the progress screen — nothing in this plan renders a number |
| WMCP-31 full suite green per commit | Global Constraints · every task's commit step |
| WMCP-32 descriptions | Task 6 test · final walkthrough (a cold session must call the tools unprompted) |

## Final verification (before pushing)

- [ ] Migrations `AddUserSettingsActiveTargetRule` and `AddWritingEntryMarkedText` are applied to prod (`dotnet ef migrations list`), and the temporary firewall rule is **removed** (`az sql server firewall-rule list … --query "[].name"` shows no `tmp-apply`).
- [ ] `cd backend && dotnet test` — 0 failures.
- [ ] `cd frontend && npx tsc --noEmit && npm run build && npx vitest run && npx playwright test` — all green.
- [ ] `git status` is clean apart from `daily-state.md` / `AGENTS.md`; no working file was swept into a commit.
- [ ] Walk the whole loop end-to-end as the writer, on the phone, against prod: connect Claude Code → `get_active_target_rule` returns null → set a rule in chat → `list_pending_writing_entries` finds the real 2026-08-16 night → `record_writing_correction` → the night disappears from pending → the History badge flips to corrected → the detail page is locked but deletable.
- [ ] Re-run the `docs/test-cases/writing-mcp-tools-test-cases.xlsx` sheet and fill in Status/Tester/Date. The 7 hard gates (WMCP-01, 03, 04, 05, 11, 12, 26) must all read Pass.
- [ ] WMCP-10 baseline still holds after deploy: `curl -s -o /dev/null -w "%{http_code}\n" -X POST https://menunest.azurewebsites.net/mcp -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'` returns **401** (it did on 2026-08-17, with `WWW-Authenticate: Bearer`). Adding tools must not open the endpoint.
- [ ] WMCP-11 on prod: from a session signed in as `thodsaphonsp@gmail.com` (`Users#05e025db-e3b4-4287-b0fa-17e8e524cb4d`), `list_pending_writing_entries` returns 0 items and `record_writing_correction` on `2e3ab6ec-097e-44c0-a7c3-602a32e8085f` is refused with "Writing entry not found."
- [ ] Push: `git push main HEAD:main` (the remote is `main`, not `origin`).

## Deferred — file these before closing #97

- The **ผลตรวจ** screen (mock frame 2) rendering the five stored blocks, incl. whitelisting the `miss`/`fix`/`hit`/`th` spans in `markedText`.
- The **ความคืบหน้า** screen (mock frame 3): 7-day pooled words-per-minute and misses-per-100-words (`missCount / wordCount × 100`, one decimal — the mock's 8/57 → `14.0`), sparklines, the monthly old-vs-new comparison, and no streak anywhere.
- Fog still open on `map.md`: draft autosave/crash-recovery, and restoring a soft-deleted entry.
