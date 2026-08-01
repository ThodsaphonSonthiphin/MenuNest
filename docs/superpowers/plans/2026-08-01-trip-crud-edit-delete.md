# Trip CRUD — edit every field, and delete (#50) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every field `CreateTripDialog` collects (name, destination, start date, day count, default travel mode) becomes changeable on an existing trip through a new `EditTripDialog`, a trip can be deleted from that dialog's footer, and neither path silently destroys scheduled stops.

**Architecture:** Frontend-heavy. A dedicated `EditTripDialog` — a **sibling** to `CreateTripDialog` that reuses its `.create-trip-dialog` CSS class but is not a mode on it — is opened by an inline-SVG pencil button in the trip-detail header (desktop top-bar **and** mobile header). It stages all five fields behind an explicit save, dirty-diffs so an unchanged save issues **no** PUT, and raises the shared `useConfirm()` modal twice: before a day-count **Shrink** that would destroy stops, and before deleting the trip. Two new domain guards land on the existing `PUT /api/trips/{id}` so the MCP path is guarded too: a stop-destroying shrink is refused unless `AllowStopLoss` is set, and a **Backdate** (a start-date change that lands in the past) is refused outright.

**Tech Stack:** React 19 + TypeScript + RTK Query + Syncfusion React (`react-popups` `Dialog`, `react-inputs` `TextBox`, `react-calendars` `DatePicker`) on the frontend; .NET 10 / EF Core / Mediator / FluentValidation / xUnit + Moq + FluentAssertions on the backend.

---

## Global Constraints

Every task's requirements implicitly include this section.

- **Issue reference on every commit.** Conventional-commit subject plus `(#50)`; only the final task closes it with `(closes #50)`.
- **Stage explicit paths only.** `git add <paths>` — never `git add -A` / `git add .`. `daily-state.md` (tracked, usually dirty) and `AGENTS.md` (untracked) must never be swept into a commit.
- **The pre-commit hook runs the FULL suite** (`frontend/.husky/pre-commit`, `set -e`): backend `dotnet build` + `dotnet test` (Release) and frontend `tsc --noEmit` + `npm run build`. Expect ~40s+ per commit. Never `--no-verify`. Every commit must leave the **entire** suite green, not just its own tests.
- **Icons are inline-SVG components, never emoji.** New icons go in `frontend/src/pages/trips/components/TripFormIcons.tsx` using that file's existing `base` spread (`viewBox 0 0 24 24`, `width/height: '1em'`, `stroke: currentColor`). `@syncfusion/react-icons` is **not installed**.
- **UI copy is Thai. Backend exception messages are English** (ADR-145) and the SPA renders them verbatim — `getErrorMessage` is a pure pass-through and there is no translation layer. Do not add one. Do not translate the four pre-existing Thai `DomainException` messages in Trips; they are a known deviation, not a precedent.
- **TypeScript config is strict about unused code**: `noUnusedLocals` and `noUnusedParameters` are both `true`, and `verbatimModuleSyntax` is `true` (type-only imports **must** use `import type`). Do not add a prop before the task that consumes it.
- **`TripsPage` is not touched by #50 at all** (ADR-141 + ADR-143). The trip card stays a single `<button>` that navigates, keeping `data-testid="trip-card"` where the Playwright e2e config expects it.
- **`CreateTripDialog` is not touched by #50.** Create/edit divergences (create allows a past start date, and allows picking a start date a daily trip will never display) are deliberate and recorded out of scope.
- **The SPA has no component/visual test harness.** `frontend/vite.config.ts` runs vitest in `environment: 'node'` with no jsdom/RTL. `tsc -b` + `npm run build` + the unit suite cannot catch rendering, layout, CSS or DOM-interaction bugs. Pure logic goes in `frontend/src/pages/trips/lib/*.ts` so it gets real vitest coverage; everything else is verified interactively in Task 9.
- **No EF migration.** Both backend changes are Application-layer only — no schema change, so none of CLAUDE.md's manual-migration ritual applies.
- **Approved visual mock** (the artifact the implementation is diffed against, Task 9): Claude Design → project `8d8d4c81-41c1-4e0a-a0b7-370b39dfbe70` → `screens/issue-50-trip-edit-delete.html`. Retrieve with `DesignSync get_file`.

### Deviations that are deliberate — do not "fix" them

| # | Thing that looks wrong | Why it is right |
|---|---|---|
| 1 | The footer split is a **modifier class** `.ctd-actions-split`, not a change to `.ctd-actions` | The mock's carried constraint says `.ctd-actions` must become `space-between`. That rule is **shared with `CreateTripDialog`**, whose two-button footer must stay `flex-end`. Applying it to the shared rule would silently restyle the create dialog, which #50 must not touch. The modifier produces the pinned visual with no create-side regression. |
| 2 | The delete button is red-outlined, unlike the muted `.se-delete` beside it | Deliberate per ADR-143: removing a stop or a place is recoverable, deleting a trip is not. A reviewer who matches it to `.se-delete` is undoing a decision. |
| 3 | MCP's `allowStopLoss` is a **required** `bool`, not an optional one | C# forbids an optional parameter before the non-optional `CancellationToken ct`. `create_trip`'s `isDaily` sets this exact precedent in the same file. The description carries the "pass false normally" semantics. |
| 4 | The shrink confirm's `confirmText` is `ลบวันและจุดแวะ`, not ADR-138's stated `'ลบ'` convention | The mock (panel D) is the approved artifact and draws the longer label. Mock wins over the ADR's copy sketch. |
| 5 | `EditTripDialog` uses plain `useState`, not react-hook-form like `CreateTripDialog` | ADR-141 requires only "staged behind an explicit save". `StopEditorDialog` is the repo's precedent for a staged-local-state edit dialog, and plain state makes the ADR-141 dirty-diff trivial. Visual output is identical — the shared `.ctd-*` classes do the work. |
| 6 | The two confirms render as raw Syncfusion (8px corners, square buttons) against the dialog's 22px teal | `useConfirm` has zero custom CSS anywhere in `frontend/src` and is mounted app-wide via `AppLayout` for Budget/Health too. Restyling it — globally or as a Trips-only variant — is explicitly out of scope. The mock draws them default on purpose. |

---

## File Structure

**Backend — changed**

| File | Responsibility after this plan |
|---|---|
| `backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripCommand.cs` | Gains a **trailing defaulted** `bool AllowStopLoss = false`, so every existing construction site still compiles. |
| `backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripHandler.cs` | Gains the two guards (stop-destroying shrink; Backdate) and an `IClock` dependency. |
| `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` | `UpdateTripBody` gains `AllowStopLoss`; `Update` forwards it. |
| `backend/src/MenuNest.McpServer/Tools/TripTools.cs` | `update_trip` gains an `allowStopLoss` parameter and both guards are described. |
| `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerTests.cs` | InMemory: refusal + past-date cases. |
| `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerRelationalTests.cs` | SQLite: the cascade actually happening when the flag is set. |

**Frontend — created**

| File | Responsibility |
|---|---|
| `frontend/src/pages/trips/lib/tripEdit.ts` | Pure logic: the edit draft, the dirty-diff, the at-risk-stop computation, name capping, total stop count. The only part of #50 that unit tests can reach. |
| `frontend/src/pages/trips/lib/tripEdit.test.ts` | Its vitest suite. |
| `frontend/src/pages/trips/components/EditTripDialog.tsx` | The whole edit surface: five staged fields, disabled-with-a-reason states, dirty-diffed save, shrink confirm, delete action, local error line. |

**Frontend — changed**

| File | Change |
|---|---|
| `frontend/src/pages/trips/TripDetailPage.tsx` | Pencil entry button in both header variants; mounts `EditTripDialog`; feeds it the cached itinerary + places. |
| `frontend/src/pages/trips/components/TripFormIcons.tsx` | `PencilIcon`, `CheckIcon`, `AlertIcon`, `ClockIcon`, `InfoIcon`, `TrashIcon`. |
| `frontend/src/pages/trips/components/TripDateEditor.tsx` | One prop: `minDate` (ADR-146 amendment). Nothing else — it must **not** get `allowStopLoss`. |
| `frontend/src/pages/trips/components/DailyToggle.tsx` | Its refusal message names the Shrink's cost and points at แก้ไข. |
| `frontend/src/pages/trips/utils/date.ts` (+ `date.test.ts`) | `thaiDate` moves here so the dialog and its confirms share one formatter. |
| `frontend/src/pages/trips/TripsPage.css` | Additive `.create-trip-dialog` rules the edit dialog needs (error box, disabled treatment, split footer, danger button). |
| `frontend/src/pages/trips/TripDetailPage.css` | The two pencil-button variants + the mobile header's new top row. |
| `frontend/src/pages/trips/trips-tokens.css` | **Global** (not page-scoped) content styles for the two confirms, which are portaled to `document.body`. |
| `frontend/src/shared/api/api.ts` | `updateTrip`'s arg type gains `allowStopLoss?: boolean`. |

---

## Task 1: Backend — `UpdateTrip` refuses a stop-destroying shrink unless `AllowStopLoss` (ADR-140)

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripHandler.cs:31-45`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs:52-54,150-152`
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs:56-65`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerTests.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerRelationalTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `UpdateTripCommand(Guid TripId, string Name, string? Destination, DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode, bool AllowStopLoss = false)`. The HTTP body accepts `allowStopLoss` (optional, defaults `false`). Task 5 sends it from the SPA.

**Context the implementer needs:** `UpdateTripHandler:44-45` removes surplus trailing `ItineraryDay` rows, and the **database** `ON DELETE CASCADE` (`StopConfiguration.cs:22`, migration `20260629104508_TripsInitial.cs:111-116`) takes their `Stop`s with them. The handler never loads the `Stop` rows, so EF's client-side cascade never fires — the DB does it. There is no soft delete on `Stop` and no restore endpoint, so the loss is unrecoverable. Today there is **no server guard whatsoever**, and no test anywhere seeds a `Stop` and then shrinks.

- [ ] **Step 1: Write the failing refusal test**

Add to `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerTests.cs` (inside the class, after `UpdateTrip_adds_trailing_days_when_extended`):

```csharp
    [Fact]
    public async Task UpdateTrip_refuses_a_shrink_that_would_delete_stops()
    {
        using var fx = new HandlerTestFixture();
        var trip = Trip.Create(fx.User.Id, "Trip", new DateOnly(2026, 11, 1), 3, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var days = new List<ItineraryDay>();
        for (var i = 0; i < 3; i++)
        {
            var d = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1).AddDays(i));
            days.Add(d);
            fx.Db.ItineraryDays.Add(d);
        }
        var place = TripPlace.Create(trip.Id, "Cafe", 18.80, 98.92, PlaceCategory.Eat);
        fx.Db.TripPlaces.Add(place);
        fx.Db.Stops.Add(Stop.Create(days[2].Id, place.Id, 0, 60, TravelMode.Drive)); // sits on day 3
        await fx.Db.SaveChangesAsync();

        // 3 -> 2 days: day 3 (and its one stop) would go.
        var cmd = new UpdateTripCommand(trip.Id, "Trip", null, new DateOnly(2026, 11, 1), 2, TravelMode.Drive);

        var thrown = await FluentActions
            .Awaiting(() => Build(fx).Handle(cmd, CancellationToken.None).AsTask())
            .Should().ThrowAsync<DomainException>();
        thrown.Which.Message.Should().Contain("1 scheduled stop").And.Contain("day(s) 3-3");

        // Nothing was persisted — the day and its stop are still there.
        (await fx.Db.ItineraryDays.CountAsync(d => d.TripId == trip.Id)).Should().Be(3);
        (await fx.Db.Stops.CountAsync()).Should().Be(1);
    }
```

`PlaceCategory` lives in `MenuNest.Domain.Enums`, which this file already imports — no new `using` is needed.

- [ ] **Step 2: Run it to verify it fails**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~UpdateTrip_refuses_a_shrink_that_would_delete_stops"
```
Expected: FAIL — no exception is thrown at all (the shrink succeeds today), so `ThrowAsync<DomainException>` fails.

- [ ] **Step 3: Add `AllowStopLoss` to the command**

Replace the whole of `backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripCommand.cs`:

```csharp
using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Trips.UpdateTrip;

/// <param name="AllowStopLoss">
/// Explicit confirmation that the caller accepts destroying the Stops on the days a lower
/// <paramref name="DayCount"/> removes (ADR-140). Trailing and defaulted on purpose: every
/// existing construction site keeps compiling, and the unsafe value is the one you have to
/// ask for. The SPA sets it only after the user confirms (ADR-138).
/// </param>
public sealed record UpdateTripCommand(
    Guid TripId, string Name, string? Destination, DateOnly StartDate, int DayCount, TravelMode DefaultTravelMode,
    bool AllowStopLoss = false)
    : ICommand<TripDto>;
```

- [ ] **Step 4: Add the guard to the handler**

In `UpdateTripHandler.cs`, insert this block **between** the `days` query (ends at `:34`) and the `// Add missing trailing days` loop (`:36-38`):

```csharp
        // Days beyond the new count are about to be removed, and their Stops cascade with them
        // (Stop->ItineraryDay FK, ON DELETE CASCADE — StopConfiguration.cs:22). Stop has no soft
        // delete and there is no restore endpoint, so that loss is unrecoverable: refuse unless
        // the caller has explicitly confirmed it (ADR-140). The SPA sets the flag only after the
        // user confirms (ADR-138); MCP exposes it so the agent path stops being silent. A shrink
        // over EMPTY days is an ordinary edit and passes without ceremony.
        var dropped = days.Skip(c.DayCount).ToList();
        if (dropped.Count > 0 && !c.AllowStopLoss)
        {
            var droppedIds = dropped.Select(d => d.Id).ToList();
            var atRisk = await _db.Stops.CountAsync(s => droppedIds.Contains(s.ItineraryDayId), ct);
            if (atRisk > 0)
                throw new DomainException(
                    $"Shrinking this trip to {c.DayCount} day(s) would delete {atRisk} scheduled stop(s) " +
                    $"on day(s) {c.DayCount + 1}-{days.Count} " +
                    $"({dropped[0].Date:yyyy-MM-dd} to {dropped[^1].Date:yyyy-MM-dd}). " +
                    "Re-send with allowStopLoss = true to confirm.");
        }
```

The message is English on purpose (ADR-145) and names the count **and** the day range, because for an MCP caller it is the only protection there is.

- [ ] **Step 5: Run the test to verify it passes**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~UpdateTripHandlerTests"
```
Expected: all PASS. `UpdateTrip_realigns_day_dates` (a 3 -> 2 shrink over **empty** days) must still pass — that is the "no ceremony when nothing is at risk" case, already covered.

- [ ] **Step 6: Write the failing cascade test on a relational provider**

The InMemory provider has no store cascade, so "the stops really do go" can only be asserted against SQLite. Add to `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerRelationalTests.cs` (after `Same_date_reschedule_is_a_noop_on_day_dates`):

```csharp
    [Fact]
    public async Task Shrink_with_AllowStopLoss_drops_the_days_and_cascades_their_stops()
    {
        var tripId = SeedTrip(new DateOnly(2026, 11, 14), 3);
        var day3 = await _db.ItineraryDays.Where(d => d.TripId == tripId)
            .OrderBy(d => d.Date).Skip(2).FirstAsync();
        var place = TripPlace.Create(tripId, "Cafe", 18.80, 98.92, PlaceCategory.Eat);
        _db.TripPlaces.Add(place);
        _db.Stops.Add(Stop.Create(day3.Id, place.Id, 0, 60, TravelMode.Drive));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await Build().Handle(
            new UpdateTripCommand(tripId, "Trip", null, new DateOnly(2026, 11, 14), 2, TravelMode.Drive,
                AllowStopLoss: true),
            CancellationToken.None);

        (await DayDatesAsync(tripId)).Should().Equal(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 15));
        (await _db.Stops.CountAsync()).Should().Be(0, "the dropped day's stops cascade-delete");
        (await _db.TripPlaces.CountAsync()).Should().Be(1, "the place pool survives — Stop->TripPlace is NoAction");
    }
```

`PlaceCategory` and `TravelMode` both come from `MenuNest.Domain.Enums`, already imported in that file.

- [ ] **Step 7: Run it**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~UpdateTripHandlerRelationalTests"
```
Expected: PASS (no production change is needed for it — the guard is bypassed by the flag and the DB does the cascade).

**If the stop count comes back `1` instead of `0`**, SQLite foreign keys are not on for this connection. `Microsoft.Data.Sqlite` enables them by default, so this is unlikely — but the fix is one string, in the constructor at `:38`: `new SqliteConnection("Filename=:memory:;Foreign Keys=True")`. Re-run the whole relational file afterwards; the other five tests seed valid FKs and must stay green.

- [ ] **Step 8: Wire the flag through the HTTP body**

In `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`, replace the `Update` action (`:52-54`):

```csharp
    [HttpPut("api/trips/{id:guid}")]
    public async Task<ActionResult<TripDto>> Update(Guid id, [FromBody] UpdateTripBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new UpdateTripCommand(id, body.Name, body.Destination, body.StartDate,
            body.DayCount, body.DefaultTravelMode, body.AllowStopLoss), ct));
```

and the body record (`:150-152`):

```csharp
public sealed record UpdateTripBody(
    string Name, string? Destination, DateOnly StartDate, int DayCount,
    MenuNest.Domain.Enums.TravelMode DefaultTravelMode,
    // Optional: a client that omits it gets `false`, so TripDateEditor's date-only PUT
    // (which never shrinks) needs no change at all — ADR-142.
    bool AllowStopLoss = false);
```

- [ ] **Step 9: Wire the flag through MCP**

In `backend/src/MenuNest.McpServer/Tools/TripTools.cs`, replace the `update_trip` tool (`:56-65`) with:

```csharp
    [McpServerTool, Description("Update a trip's fields (full replace — passing null for destination CLEARS it). WARNING: lowering dayCount deletes the trailing itinerary days AND their stops (cascade). The server REFUSES such a shrink unless allowStopLoss is true, and the refusal names how many stops and which days would be lost — read it, tell the user, and only re-send with allowStopLoss=true if they accept that loss. It cannot be undone.")]
    public async Task<TripDto> update_trip(
        [Description("Trip ID")] Guid tripId,
        [Description("Trip name")] string name,
        [Description("Optional destination")] string? destination,
        [Description("Start date, YYYY-MM-DD")] DateOnly startDate,
        [Description("Number of itinerary days (1 or more); lowering removes trailing days and their stops")] int dayCount,
        [Description("Default travel mode: Drive, Walk, or Transit")] TravelMode defaultTravelMode,
        [Description("Pass false normally. Only true to CONFIRM deleting the stops on the days a lower dayCount removes — unrecoverable.")] bool allowStopLoss,
        CancellationToken ct)
        => await mediator.Send(new UpdateTripCommand(tripId, name, destination, startDate, dayCount, defaultTravelMode, allowStopLoss), ct);
```

`allowStopLoss` is a required parameter, not an optional one: C# forbids an optional parameter before the non-optional `CancellationToken ct`, and `create_trip`'s `isDaily` (`:52`) is the same shape in this same file.

- [ ] **Step 10: Build and run the whole backend suite**

```bash
cd backend
dotnet build
dotnet test
```
Expected: PASS, all four test projects.

- [ ] **Step 11: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripCommand.cs \
        backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripHandler.cs \
        backend/src/MenuNest.WebApi/Controllers/TripsController.cs \
        backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerRelationalTests.cs
git commit -m "feat(trips): UpdateTrip refuses a stop-destroying shrink unless AllowStopLoss (#50)"
```

---

## Task 2: Backend — `UpdateTrip` refuses a Backdate (ADR-146)

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripHandler.cs:11-29`
- Modify: `backend/src/MenuNest.McpServer/Tools/TripTools.cs` (the `startDate` `[Description]` on `update_trip`)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerTests.cs:14-15` (the `Build` helper) + three new tests
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerRelationalTests.cs:55` (the `Build` helper)

**Interfaces:**
- Consumes: `UpdateTripCommand` from Task 1 (unchanged shape).
- Produces: `UpdateTripHandler(IApplicationDbContext, IUserProvisioner, IValidator<UpdateTripCommand>, IClock)` — a **fourth** constructor parameter. Both test `Build` helpers must pass a **fixed** clock.

**Context the implementer needs:** `RetimeStopToHourHandler` — the only other writer of `Trip.StartDate`, reusing the same `DayRealigner` — already guards past dates at `:36-41`. `UpdateTrip` does not, and the asymmetry is what this closes. The rule governs **where the date lands, never which direction it moved**: `14 Nov -> 12 Nov` is fine while both are ahead, which is what keeps the existing `Backward_nudge_realigns_without_collision` relational test valid. Critically, `UpdateTrip` is a **full replace** — every save re-sends `StartDate` — so a naive "refuse if `c.StartDate` is past" rule would break *renaming* last month's trip. Only a **changed**-date check works. `IClock` is already registered in DI (`RetimeStopToHourHandler` takes it).

**Never wire the system clock into these tests.** Every date in both test files is a hardcoded `2026-11-x` / `2026-12-x`; a real clock turns the whole suite into a time bomb that detonates in December 2026.

- [ ] **Step 1: Write the three failing tests**

Add to `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerTests.cs`:

```csharp
    [Fact]
    public async Task UpdateTrip_refuses_moving_the_start_date_into_the_past()
    {
        using var fx = new HandlerTestFixture();          // clock fixed at 2026-01-01 UTC
        var trip = Trip.Create(fx.User.Id, "Trip", new DateOnly(2026, 11, 1), 2, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        for (var i = 0; i < 2; i++)
            fx.Db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1).AddDays(i)));
        await fx.Db.SaveChangesAsync();

        var cmd = new UpdateTripCommand(trip.Id, "Trip", null, new DateOnly(2025, 12, 1), 2, TravelMode.Drive);

        await FluentActions.Awaiting(() => Build(fx).Handle(cmd, CancellationToken.None).AsTask())
            .Should().ThrowAsync<DomainException>()
            .WithMessage("*already in the past*");
    }

    [Fact]
    public async Task UpdateTrip_allows_renaming_a_trip_whose_start_date_is_already_past()
    {
        using var fx = new HandlerTestFixture();          // clock fixed at 2026-01-01 UTC
        var trip = Trip.Create(fx.User.Id, "Old", new DateOnly(2025, 3, 1), 2, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        for (var i = 0; i < 2; i++)
            fx.Db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, new DateOnly(2025, 3, 1).AddDays(i)));
        await fx.Db.SaveChangesAsync();

        // The full-replace PUT re-sends the same (past) start date; only the name changed.
        var cmd = new UpdateTripCommand(trip.Id, "Renamed", null, new DateOnly(2025, 3, 1), 2, TravelMode.Drive);
        var dto = await Build(fx).Handle(cmd, CancellationToken.None);

        dto.Name.Should().Be("Renamed");
        dto.StartDate.Should().Be(new DateOnly(2025, 3, 1));
    }

    [Fact]
    public async Task UpdateTrip_allows_a_backward_move_that_still_lands_in_the_future()
    {
        using var fx = new HandlerTestFixture();          // clock fixed at 2026-01-01 UTC
        var trip = Trip.Create(fx.User.Id, "Trip", new DateOnly(2026, 11, 14), 2, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        for (var i = 0; i < 2; i++)
            fx.Db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 14).AddDays(i)));
        await fx.Db.SaveChangesAsync();

        // What is governed is where the date LANDS, not which direction it moved.
        var cmd = new UpdateTripCommand(trip.Id, "Trip", null, new DateOnly(2026, 11, 12), 2, TravelMode.Drive);
        var dto = await Build(fx).Handle(cmd, CancellationToken.None);

        dto.StartDate.Should().Be(new DateOnly(2026, 11, 12));
    }
```

- [ ] **Step 2: Run them to verify the first one fails**

```bash
cd backend
dotnet test tests/MenuNest.Application.UnitTests --filter "FullyQualifiedName~UpdateTripHandlerTests"
```
Expected: `UpdateTrip_refuses_moving_the_start_date_into_the_past` FAILS (no exception thrown). The other two PASS already — they are the regression net for the guard about to be added.

- [ ] **Step 3: Inject `IClock` and add the guard**

In `UpdateTripHandler.cs`, replace the field block and constructor (`:13-18`):

```csharp
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdateTripCommand> _validator;
    private readonly IClock _clock;

    public UpdateTripHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdateTripCommand> validator, IClock clock)
    { _db = db; _users = users; _validator = validator; _clock = clock; }
```

and insert the guard immediately after the trip is loaded — **before** `trip.UpdateDetails(...)` at `:28`, because `trip.Reschedule` overwrites the value being compared:

```csharp
        // Guard: the start date may move, but never onto a date already in the past — a
        // Backdate (ADR-146). What is governed is where the date LANDS, never which direction
        // it moved, so 14 Nov -> 12 Nov is fine while both are ahead. An UNCHANGED date always
        // passes, which is what keeps renaming / re-counting a trip that already started
        // working under this full-replace PUT. Same floor and same reasoning as
        // RetimeStopToHourHandler.cs:36-41 — one day of slack keeps a legitimate viewer-local
        // "today" that is still UTC-yesterday from being falsely rejected (MenuNest is Thai-first
        // at UTC+7, but it is a *travel* app).
        if (c.StartDate != trip.StartDate &&
            c.StartDate < DateOnly.FromDateTime(_clock.UtcNow).AddDays(-1))
            throw new DomainException("Cannot move a trip to a start date that is already in the past.");
```

- [ ] **Step 4: Fix both `Build` helpers**

`UpdateTripHandlerTests.cs:14-15` — the fixture already exposes a clock fixed at 2026-01-01:

```csharp
    private static UpdateTripHandler Build(HandlerTestFixture fx)
        => new(fx.Db, fx.UserProvisioner.Object, new UpdateTripValidator(), fx.Clock);
```

`UpdateTripHandlerRelationalTests.cs:55` — this class builds the handler bare, so it needs its own. Add the field next to `_users` and use it:

```csharp
    // Fixed, never the system clock: every date in this file is a hardcoded 2026-11-x, so a
    // real clock would turn the suite into a time bomb that detonates in December 2026 (ADR-146).
    private readonly IClock _clock = new FixedClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private UpdateTripHandler Build() => new(_db, _users.Object, new UpdateTripValidator(), _clock);
```

`IClock` comes from `MenuNest.Application.Abstractions` and `FixedClock` from `MenuNest.Application.UnitTests.Support` — both `using`s are already at the top of that file.

- [ ] **Step 5: Run the full backend suite**

```bash
cd backend
dotnet build
dotnet test
```
Expected: PASS. Watch specifically that `Backward_nudge_realigns_without_collision` (relational, `14 Nov -> 12 Nov`) is still green — it is the test that proves the guard reads "where it lands", not "which way it moved".

- [ ] **Step 6: Say so in the MCP tool description**

In `TripTools.cs`, on `update_trip`, replace the `startDate` parameter description:

```csharp
        [Description("Start date, YYYY-MM-DD. It may move FORWARD freely, but CHANGING it to a date already in the past is refused; re-sending the trip's existing past date is fine.")] DateOnly startDate,
```

MCP inherits the refusal with no override flag — an exact mirror of `RetimeStopToHour`, which has none either.

- [ ] **Step 7: Build and commit**

```bash
cd backend && dotnet build && dotnet test
cd ..
git add backend/src/MenuNest.Application/UseCases/Trips/UpdateTrip/UpdateTripHandler.cs \
        backend/src/MenuNest.McpServer/Tools/TripTools.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerTests.cs \
        backend/tests/MenuNest.Application.UnitTests/Trips/UpdateTripHandlerRelationalTests.cs
git commit -m "feat(trips): UpdateTrip refuses a Backdate, matching RetimeStopToHour (#50)"
```

---

## Task 3: Frontend — the pure edit-logic module

**Files:**
- Create: `frontend/src/pages/trips/lib/tripEdit.ts`
- Create: `frontend/src/pages/trips/lib/tripEdit.test.ts`
- Modify: `frontend/src/pages/trips/utils/date.ts`
- Modify: `frontend/src/pages/trips/utils/date.test.ts`

**Interfaces:**
- Consumes: `TripDto`, `ItineraryDayDto`, `TravelMode` from `../../../shared/api/api`.
- Produces, all used by Tasks 4–7:
  - `interface TripEditDraft {name: string; destination: string; startDate: string; dayCount: number; defaultTravelMode: TravelMode}`
  - `draftFromTrip(trip: TripDto): TripEditDraft`
  - `normalizeDraft(d: TripEditDraft): TripEditDraft`
  - `isDraftDirty(d: TripEditDraft, trip: TripDto): boolean`
  - `interface ShrinkLossStop {name: string; isVisited: boolean}`
  - `interface ShrinkLoss {dayFrom: number; dayTo: number; dateFrom: string; dateTo: string; stops: ShrinkLossStop[]; visitedCount: number}`
  - `shrinkLoss(days: ItineraryDayDto[], placeNameById: Record<string, string>, newDayCount: number): ShrinkLoss | null`
  - `capNames<T>(items: T[], max?: number): {shown: T[]; moreCount: number}`
  - `totalStops(days: ItineraryDayDto[]): number`
  - and from `../utils/date`: `thaiDate(d: Date): string`

**Why this file exists:** it is the only part of #50 that automated tests can actually reach. `tsc -b` and `npm run build` cannot catch a wrong at-risk count or a broken dirty-diff; vitest can.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/pages/trips/lib/tripEdit.test.ts`:

```ts
import {describe, expect, it} from 'vitest'
import type {ItineraryDayDto, TripDto} from '../../../shared/api/api'
import {capNames, draftFromTrip, isDraftDirty, normalizeDraft, shrinkLoss, totalStops} from './tripEdit'

const trip: TripDto = {
  id: 't1',
  name: 'เที่ยวเชียงใหม่',
  destination: 'เชียงใหม่',
  startDate: '2026-08-01',
  dayCount: 3,
  defaultTravelMode: 'Drive',
  isDaily: false,
}

function day(id: string, date: string, stops: {id: string; placeId: string; visited?: boolean}[]): ItineraryDayDto {
  return {
    id,
    date,
    dayStartTime: '09:00:00',
    useCurrentTimeAsStart: false,
    stops: stops.map((s, i) => ({
      id: s.id,
      tripPlaceId: s.placeId,
      sequence: i,
      dwellMinutes: 60,
      travelModeToReach: 'Drive' as const,
      legToReach: null,
      isVisited: s.visited ?? false,
    })),
  }
}

const NAMES = {p1: 'วัดพระธาตุดอยสุเทพ', p2: 'ร้านกาแฟ Ristr8to', p3: 'ไนท์บาซาร์'}

describe('draftFromTrip', () => {
  it('maps a null destination to an empty string', () => {
    expect(draftFromTrip({...trip, destination: null}).destination).toBe('')
  })

  it('trims a date-time start date down to yyyy-MM-dd', () => {
    expect(draftFromTrip({...trip, startDate: '2026-08-01T00:00:00'}).startDate).toBe('2026-08-01')
  })
})

describe('isDraftDirty', () => {
  it('is false for an untouched draft', () => {
    expect(isDraftDirty(draftFromTrip(trip), trip)).toBe(false)
  })

  it('is false when only whitespace was added, because the server trims too', () => {
    const d = normalizeDraft({...draftFromTrip(trip), name: '  เที่ยวเชียงใหม่  '})
    expect(isDraftDirty(d, trip)).toBe(false)
  })

  it('is false when an already-empty destination is blanked differently', () => {
    const t = {...trip, destination: null}
    expect(isDraftDirty({...draftFromTrip(t), destination: '   '}, t)).toBe(false)
  })

  it.each([
    ['name', {name: 'อื่น'}],
    ['destination', {destination: 'ลำปาง'}],
    ['startDate', {startDate: '2026-08-02'}],
    ['dayCount', {dayCount: 2}],
    ['defaultTravelMode', {defaultTravelMode: 'Walk' as const}],
  ])('is true when %s changed', (_label, patch) => {
    expect(isDraftDirty({...draftFromTrip(trip), ...patch}, trip)).toBe(true)
  })

  it('is true when a destination is cleared', () => {
    expect(isDraftDirty({...draftFromTrip(trip), destination: ''}, trip)).toBe(true)
  })

  it('compares the start date against a date-time server value correctly', () => {
    const t = {...trip, startDate: '2026-08-01T00:00:00'}
    expect(isDraftDirty(draftFromTrip(t), t)).toBe(false)
  })
})

describe('shrinkLoss', () => {
  const days = [
    day('d1', '2026-08-01', [{id: 's1', placeId: 'p1'}]),
    day('d2', '2026-08-02', []),
    day('d3', '2026-08-03', [{id: 's2', placeId: 'p2', visited: true}, {id: 's3', placeId: 'p3'}]),
  ]

  it('is null when the itinerary is not loaded', () => {
    expect(shrinkLoss([], NAMES, 1)).toBeNull()
  })

  it('is null when the day count grows', () => {
    expect(shrinkLoss(days, NAMES, 5)).toBeNull()
  })

  it('is null when the day count is unchanged', () => {
    expect(shrinkLoss(days, NAMES, 3)).toBeNull()
  })

  it('is null when the dropped days hold no stops', () => {
    const empty = [days[0], day('d2', '2026-08-02', [])]
    expect(shrinkLoss(empty, NAMES, 1)).toBeNull()
  })

  it('reports the day range, dates, names and visited count of a real loss', () => {
    const loss = shrinkLoss(days, NAMES, 2)!
    expect(loss.dayFrom).toBe(3)
    expect(loss.dayTo).toBe(3)
    expect(loss.dateFrom).toBe('2026-08-03')
    expect(loss.dateTo).toBe('2026-08-03')
    expect(loss.stops.map((s) => s.name)).toEqual(['ร้านกาแฟ Ristr8to', 'ไนท์บาซาร์'])
    expect(loss.visitedCount).toBe(1)
  })

  it('spans several dropped days and skips the empty one in between', () => {
    const loss = shrinkLoss(days, NAMES, 1)!
    expect(loss.dayFrom).toBe(2)
    expect(loss.dayTo).toBe(3)
    expect(loss.dateFrom).toBe('2026-08-02')
    expect(loss.dateTo).toBe('2026-08-03')
    expect(loss.stops).toHaveLength(2)
  })

  it('falls back to a generic label when a place name is missing', () => {
    expect(shrinkLoss(days, {}, 2)!.stops[0].name).toBe('สถานที่')
  })

  it('takes the drop set by index, not by date', () => {
    // A single-day current-time-start trip is served with day[0].date projected to the
    // viewer's today, so date matching would pick the wrong rows.
    const projected = [day('d1', '2030-01-01', [{id: 's1', placeId: 'p1'}]), days[2]]
    const loss = shrinkLoss(projected, NAMES, 1)!
    expect(loss.stops.map((s) => s.name)).toEqual(['ร้านกาแฟ Ristr8to', 'ไนท์บาซาร์'])
  })
})

describe('capNames', () => {
  it('returns everything with no overflow under the cap', () => {
    expect(capNames([1, 2, 3], 5)).toEqual({shown: [1, 2, 3], moreCount: 0})
  })

  it('caps and counts the overflow', () => {
    expect(capNames([1, 2, 3, 4, 5, 6, 7], 5)).toEqual({shown: [1, 2, 3, 4, 5], moreCount: 2})
  })
})

describe('totalStops', () => {
  it('sums every day', () => {
    expect(totalStops([
      day('d1', '2026-08-01', [{id: 's1', placeId: 'p1'}]),
      day('d2', '2026-08-02', [{id: 's2', placeId: 'p2'}, {id: 's3', placeId: 'p3'}]),
    ])).toBe(3)
  })

  it('is zero for an unloaded itinerary', () => {
    expect(totalStops([])).toBe(0)
  })
})
```

Add to `frontend/src/pages/trips/utils/date.test.ts` — extend its existing import from `./date` with `thaiDate`, then append:

```ts
describe('thaiDate', () => {
  it('renders a Buddhist-era short Thai date', () => {
    // 2026 CE -> 2569 BE. Only the year and day are asserted, because month abbreviations
    // vary between ICU builds.
    expect(thaiDate(new Date(2026, 7, 1))).toContain('2569')
  })

  it('does not shift the day across a timezone boundary', () => {
    expect(thaiDate(new Date(2026, 7, 1))).toContain('1')
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd frontend
npx vitest run src/pages/trips/lib/tripEdit.test.ts src/pages/trips/utils/date.test.ts
```
Expected: FAIL — `Failed to resolve import "./tripEdit"`, and `thaiDate` is not exported from `./date`.

- [ ] **Step 3: Add `thaiDate` to the shared date utils**

Append to `frontend/src/pages/trips/utils/date.ts`:

```ts
/** Thai Buddhist-era short date, e.g. "1 ส.ค. 2569" — the app-wide trip date label. */
export function thaiDate(d: Date): string {
  return d.toLocaleDateString('th-TH', {day: 'numeric', month: 'short', year: 'numeric'})
}
```

`CreateTripDialog.tsx:59-61` keeps its identical private copy. That duplication is deliberate: #50 does not touch the create dialog, and moving a function out of it is a change no automated gate in this repo can visually verify.

- [ ] **Step 4: Write the module**

Create `frontend/src/pages/trips/lib/tripEdit.ts`:

```ts
// frontend/src/pages/trips/lib/tripEdit.ts
//
// Pure logic behind EditTripDialog (issue #50). Kept out of the component on purpose:
// the SPA's vitest runs in environment:'node' with no jsdom, so a component's rendering
// is untestable here — but the draft diffing and the at-risk-stop arithmetic, which are
// the parts that can silently destroy data, are not.
import type {ItineraryDayDto, TravelMode, TripDto} from '../../../shared/api/api'

/** The five fields the edit form stages. Mirrors what PUT /api/trips/{id} carries. */
export interface TripEditDraft {
  name: string
  destination: string // '' rather than null — a text input's empty value
  startDate: string // "yyyy-MM-dd"
  dayCount: number
  defaultTravelMode: TravelMode
}

export function draftFromTrip(trip: TripDto): TripEditDraft {
  return {
    name: trip.name,
    destination: trip.destination ?? '',
    startDate: trip.startDate.slice(0, 10),
    dayCount: trip.dayCount,
    defaultTravelMode: trip.defaultTravelMode,
  }
}

/** Trim the free-text fields exactly as the server does — Trip.UpdateDetails trims both. */
export function normalizeDraft(d: TripEditDraft): TripEditDraft {
  return {...d, name: d.name.trim(), destination: d.destination.trim()}
}

/** '' / '   ' / null all mean "no destination"; Trip.Destination stores null. */
function normDest(v: string | null | undefined): string | null {
  const t = (v ?? '').trim()
  return t.length ? t : null
}

/**
 * True when the draft differs from the trip in any field the PUT carries.
 *
 * A `false` here means the save must issue NO PUT at all (ADR-141): updateTrip invalidates
 * {type:'TripItinerary', id} on EVERY call, and a getItinerary refetch re-bills the Google
 * Routes API and re-fetches Weather. A no-op save would be a cost this feature newly introduces.
 */
export function isDraftDirty(d: TripEditDraft, trip: TripDto): boolean {
  return (
    d.name.trim() !== trip.name ||
    normDest(d.destination) !== normDest(trip.destination) ||
    d.startDate !== trip.startDate.slice(0, 10) ||
    d.dayCount !== trip.dayCount ||
    d.defaultTravelMode !== trip.defaultTravelMode
  )
}

export interface ShrinkLossStop {
  name: string
  isVisited: boolean
}

export interface ShrinkLoss {
  dayFrom: number // 1-based number of the first day that goes
  dayTo: number // 1-based number of the last day that goes
  dateFrom: string // "yyyy-MM-dd"
  dateTo: string
  stops: ShrinkLossStop[]
  visitedCount: number
}

/**
 * What shrinking to `newDayCount` would destroy — or null when nothing is at risk.
 *
 * Null covers three cases that all mean "save straight through": the itinerary is not
 * loaded, this is not a shrink, or the dropped days are empty. ADR-138 fires the confirm
 * only on real loss; a red modal on a harmless 5 -> 3 over empty days trains tap-through
 * and costs the signal on the shrink that destroys six stops.
 *
 * The drop set is taken BY INDEX, never by date: GetItineraryHandler projects a single-day
 * current-time-start trip's date to the viewer's today, so date matching is unsafe.
 */
export function shrinkLoss(
  days: ItineraryDayDto[],
  placeNameById: Record<string, string>,
  newDayCount: number,
): ShrinkLoss | null {
  if (days.length === 0 || newDayCount >= days.length) return null
  const dropped = days.slice(newDayCount)
  const stops: ShrinkLossStop[] = dropped.flatMap((d) =>
    d.stops.map((s) => ({
      name: placeNameById[s.tripPlaceId] ?? 'สถานที่',
      isVisited: s.isVisited,
    })),
  )
  if (stops.length === 0) return null
  return {
    dayFrom: newDayCount + 1,
    dayTo: days.length,
    dateFrom: dropped[0].date.slice(0, 10),
    dateTo: dropped[dropped.length - 1].date.slice(0, 10),
    stops,
    visitedCount: stops.filter((s) => s.isVisited).length,
  }
}

/** Cap a list for the 420px confirm dialog: the first `max`, plus an overflow count. */
export function capNames<T>(items: T[], max = 5): {shown: T[]; moreCount: number} {
  return {shown: items.slice(0, max), moreCount: Math.max(0, items.length - max)}
}

/** Total stops across the cached itinerary — the "M จุดแวะ" in the delete confirm. */
export function totalStops(days: ItineraryDayDto[]): number {
  return days.reduce((n, d) => n + d.stops.length, 0)
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd frontend
npx vitest run src/pages/trips/lib/tripEdit.test.ts src/pages/trips/utils/date.test.ts
```
Expected: PASS.

- [ ] **Step 6: Typecheck, build, commit**

```bash
cd frontend && npx tsc -b && npm run build && npm run test -- --run
cd ..
git add frontend/src/pages/trips/lib/tripEdit.ts \
        frontend/src/pages/trips/lib/tripEdit.test.ts \
        frontend/src/pages/trips/utils/date.ts \
        frontend/src/pages/trips/utils/date.test.ts
git commit -m "feat(trips): pure trip-edit draft, dirty-diff and shrink-loss logic (#50)"
```

---

## Task 4: Frontend — `EditTripDialog` and its two header entry points (ADR-141)

**Files:**
- Create: `frontend/src/pages/trips/components/EditTripDialog.tsx`
- Modify: `frontend/src/pages/trips/components/TripFormIcons.tsx` (append)
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx:1-32,104-118,180-201,258-260`
- Modify: `frontend/src/pages/trips/TripsPage.css` (append)
- Modify: `frontend/src/pages/trips/TripDetailPage.css` (append)

**Interfaces:**
- Consumes: `TripEditDraft`, `draftFromTrip`, `normalizeDraft`, `isDraftDirty` from `../lib/tripEdit`; `ymdToDate`, `dateToYmd`, `endDate`, `thaiDate` from `../utils/date`.
- Produces: `EditTripDialog({trip, onClose}: {trip: TripDto; onClose: () => void})`. Tasks 5–8 add the props `days`, `places` and `overrideDate` to this signature — **do not add them now**, `noUnusedParameters` is on.
- Produces icons: `PencilIcon`, `CheckIcon`, `AlertIcon` in `TripFormIcons.tsx`.

**Deliverable:** a normal trip's name, destination, start date, day count and travel mode are all editable and savable from the trip-detail page on both desktop and mobile. This is the first task that can be verified in a browser.

**Reference:** the approved mock, panel **A** (both entry points) and panel **B** (the dialog's normal state).

- [ ] **Step 1: Add the three icons**

Append to `frontend/src/pages/trips/components/TripFormIcons.tsx`:

```tsx
/** Pencil — the trip-edit entry button and the edit dialog's header badge. */
export function PencilIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M12 20h9" />
      <path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z" />
    </svg>
  )
}

/** Check — the edit dialog's save button. */
export function CheckIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <polyline points="20 6 9 17 4 12" />
    </svg>
  )
}

/** Warning triangle — the edit dialog's save-failure box. */
export function AlertIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
      <line x1="12" y1="9" x2="12" y2="13" />
      <line x1="12" y1="17" x2="12.01" y2="17" />
    </svg>
  )
}
```

- [ ] **Step 2: Write the dialog**

Create `frontend/src/pages/trips/components/EditTripDialog.tsx`:

```tsx
// frontend/src/pages/trips/components/EditTripDialog.tsx
import {useMemo, useState, type ReactNode} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {TextBox} from '@syncfusion/react-inputs'
import {DatePicker} from '@syncfusion/react-calendars'
import type {DatePickerChangeEvent} from '@syncfusion/react-calendars'
import {useUpdateTripMutation, type TravelMode, type TripDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {draftFromTrip, isDraftDirty, normalizeDraft, type TripEditDraft} from '../lib/tripEdit'
import {dateToYmd, endDate, thaiDate, ymdToDate} from '../utils/date'
import {
  AlertIcon,
  ArrowRightIcon,
  CarIcon,
  CheckIcon,
  MapPinIcon,
  MinusIcon,
  PencilIcon,
  PlusIcon,
  TransitIcon,
  WalkIcon,
} from './TripFormIcons'

// Same three values and labels as CreateTripDialog — the backend TravelMode enum.
const MODES: {label: string; value: TravelMode; icon: ReactNode}[] = [
  {label: 'รถยนต์', value: 'Drive', icon: <CarIcon />},
  {label: 'ขนส่งสาธารณะ', value: 'Transit', icon: <TransitIcon />},
  {label: 'เดิน', value: 'Walk', icon: <WalkIcon />},
]

const MIN_DAYS = 1
const MAX_DAYS = 60

/**
 * Edit an existing trip's five fields (issue #50, ADR-141).
 *
 * A dedicated SIBLING of CreateTripDialog, not a mode on it: create and edit diverge in
 * title, submit label, defaults, mutation, success behaviour, the day-count guard — and
 * edit drops the isDaily switch entirely, because ADR-137 forbids IsDaily on the
 * full-replace UpdateTrip and DailyToggle stays on the header. The two share the
 * `.create-trip-dialog` CSS class so they cannot drift visually; the Syncfusion Dialog is
 * portaled to document.body and cannot see the page-scoped .trip-detail tokens, so each
 * dialog family declares its own palette there.
 *
 * Every field is STAGED behind an explicit save (ADR-138 requires it for day count, and a
 * form where one field behaves differently from its neighbours is worse than either
 * consistent option). The save is dirty-diffed, errors stay local with the dialog open,
 * and cancel closes with no warning — what is lost is typed text, not data.
 */
export function EditTripDialog({trip, onClose}: {trip: TripDto; onClose: () => void}) {
  const [draft, setDraft] = useState<TripEditDraft>(() => draftFromTrip(trip))
  const [nameError, setNameError] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [updateTrip, {isLoading}] = useUpdateTripMutation()

  const set = <K extends keyof TripEditDraft>(k: K, v: TripEditDraft[K]) =>
    setDraft((d) => ({...d, [k]: v}))

  // Live end-date summary — most useful precisely when changing the day count.
  const endLabel = useMemo(() => {
    const e = endDate(ymdToDate(draft.startDate), draft.dayCount)
    return e ? thaiDate(e) : null
  }, [draft.startDate, draft.dayCount])

  const save = async () => {
    setSaveError(null)
    const d = normalizeDraft(draft)
    if (!d.name) {
      setNameError('กรุณากรอกชื่อทริป')
      return
    }
    setNameError(null)
    // Dirty-diff (ADR-141): an unchanged save issues NO PUT. updateTrip invalidates
    // TripItinerary on every call, and that refetch re-bills Google Routes + Weather.
    if (!isDraftDirty(d, trip)) {
      onClose()
      return
    }
    try {
      await updateTrip({
        id: trip.id,
        name: d.name,
        destination: d.destination || null,
        startDate: d.startDate,
        dayCount: d.dayCount,
        defaultTravelMode: d.defaultTravelMode,
      }).unwrap()
      onClose()
    } catch (e) {
      // The dialog STAYS OPEN on failure and shows the message inside itself. Backend
      // messages are English and are rendered verbatim (ADR-145).
      setSaveError(getErrorMessage(e))
    }
  }

  const header = (
    <div className="ctd-head">
      <span className="ctd-head-badge">
        <PencilIcon />
      </span>
      <div className="ctd-head-text">
        <span className="ctd-head-title">แก้ไขทริป</span>
        <span className="ctd-head-sub">เปลี่ยนรายละเอียดของทริปนี้</span>
      </div>
    </div>
  )

  return (
    <Dialog
      open
      onClose={onClose}
      modal
      className="create-trip-dialog"
      header={header}
      style={{width: 'min(460px, calc(100vw - 24px))'}}
    >
      <form
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
        noValidate
        className="ctd-form"
      >
        {/* Trip name */}
        <div className="ctd-field">
          <label className="ctd-label">
            ชื่อทริป <span className="ctd-req">*</span>
          </label>
          <TextBox
            value={draft.name}
            placeholder="เช่น เชียงใหม่ 3 วัน"
            onChange={(e) => set('name', e.value ?? '')}
          />
          {nameError && <p className="ctd-error">{nameError}</p>}
        </div>

        {/* Destination — pin lead icon */}
        <div className="ctd-field">
          <label className="ctd-label">ปลายทาง</label>
          <div className="ctd-pin">
            <span className="ctd-pin-ico">
              <MapPinIcon />
            </span>
            <TextBox
              value={draft.destination}
              placeholder="Chiang Mai"
              onChange={(e) => set('destination', e.value ?? '')}
            />
          </div>
        </div>

        {/* Start date + day count — two columns. No daily switch: ADR-137/141. */}
        <div className="ctd-row2">
          <div className="ctd-field">
            <label className="ctd-label">
              วันเริ่ม <span className="ctd-req">*</span>
            </label>
            <DatePicker
              value={ymdToDate(draft.startDate)}
              format="dd MMM yyyy"
              onChange={(e: DatePickerChangeEvent) => {
                const v = dateToYmd(e.value)
                if (v) set('startDate', v)
              }}
            />
          </div>

          <div className="ctd-field">
            <label className="ctd-label">
              จำนวนวัน <span className="ctd-req">*</span>
            </label>
            <div className="ctd-stepper">
              <button
                type="button"
                className="ctd-step"
                aria-label="ลดจำนวนวัน"
                disabled={draft.dayCount <= MIN_DAYS}
                onClick={() => set('dayCount', Math.max(MIN_DAYS, draft.dayCount - 1))}
              >
                <MinusIcon />
              </button>
              <span className="ctd-step-val" aria-live="polite">
                {draft.dayCount}
              </span>
              <button
                type="button"
                className="ctd-step"
                aria-label="เพิ่มจำนวนวัน"
                disabled={draft.dayCount >= MAX_DAYS}
                onClick={() => set('dayCount', Math.min(MAX_DAYS, draft.dayCount + 1))}
              >
                <PlusIcon />
              </button>
            </div>
          </div>
        </div>

        {/* Live end-date summary */}
        {endLabel && (
          <div className="ctd-summary">
            <span className="ctd-summary-ico">
              <ArrowRightIcon />
            </span>
            <span>
              สิ้นสุด <b>{endLabel}</b> · รวม <b>{draft.dayCount} วัน</b>
            </span>
          </div>
        )}

        {/* Primary travel mode — tiles */}
        <div className="ctd-field">
          <label className="ctd-label">การเดินทางหลัก</label>
          <div className="ctd-modes" role="radiogroup" aria-label="การเดินทางหลัก">
            {MODES.map((m) => (
              <button
                type="button"
                key={m.value}
                role="radio"
                aria-checked={draft.defaultTravelMode === m.value}
                className={`ctd-mode${draft.defaultTravelMode === m.value ? ' active' : ''}`}
                onClick={() => set('defaultTravelMode', m.value)}
              >
                <span className="ctd-mode-ico">{m.icon}</span>
                <span className="ctd-mode-lab">{m.label}</span>
              </button>
            ))}
          </div>
        </div>

        {saveError && (
          <div className="ctd-errbox">
            <AlertIcon />
            <span>{saveError}</span>
          </div>
        )}

        <div className="ctd-actions">
          <button type="button" className="ctd-btn ctd-btn-ghost" onClick={onClose}>
            ยกเลิก
          </button>
          <button type="submit" className="ctd-btn ctd-btn-primary" disabled={isLoading}>
            {isLoading ? (
              '…'
            ) : (
              <>
                <CheckIcon /> บันทึก
              </>
            )}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
```

- [ ] **Step 3: Add the dialog's extra CSS**

Append to `frontend/src/pages/trips/TripsPage.css`, immediately after the existing `.create-trip-dialog .ctd-btn:focus-visible` block:

```css
/* ============================================================
   Edit-trip dialog extras (issue #50, ADR-141).
   EditTripDialog reuses .create-trip-dialog wholesale so the two cannot drift; only
   the pieces the create form does not have are declared here.
   ============================================================ */

/* Save-failure box — the backend message is English and is shown verbatim (ADR-145). */
.create-trip-dialog .ctd-errbox {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin: 0;
  padding: 9px 11px;
  border: 1px solid #fecaca;
  border-radius: 10px;
  background: #fef2f2;
  color: #991b1b;
  font-size: 12.5px;
  line-height: 1.5;
}
.create-trip-dialog .ctd-errbox svg {
  width: 15px;
  height: 15px;
  flex: none;
  margin-top: 1px;
  color: var(--error);
}
```

- [ ] **Step 4: Add the entry-point CSS**

Append to `frontend/src/pages/trips/TripDetailPage.css`:

```css
/* ============================================================
   Edit-trip entry point (issue #50, ADR-141) — an explicit inline-SVG pencil BUTTON in
   both header variants: light-on-dark in the desktop .trip-topbar, teal-on-white in the
   mobile .trip-detail-header. A record-level action, so it is a real button rather than
   ADR-012's tap-the-value treatment; a plain value is a weak affordance and this is a
   brand-new capability with zero existing discoverability.
   ============================================================ */
.trip-edit-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: none;
  width: 30px;
  height: 30px;
  padding: 0;
  border-radius: 9px;
  cursor: pointer;
  transition: background 0.14s ease, border-color 0.14s ease, color 0.14s ease;
}
.trip-edit-btn svg { width: 15px; height: 15px; }
.trip-edit-btn:focus-visible { outline: 2px solid var(--teal); outline-offset: 2px; }

/* Desktop dark top-bar. The row is align-items:baseline, so re-center this control. */
.trip-topbar .trip-edit-btn {
  margin-left: auto;
  align-self: center;
  border: 1px solid rgba(255, 255, 255, 0.22);
  background: rgba(255, 255, 255, 0.08);
  color: #dbe6f0;
}
.trip-topbar .trip-edit-btn:hover { background: rgba(255, 255, 255, 0.16); color: #fff; }

/* Mobile light header — the name/meta block and the pencil share one top row. */
.trip-detail-headtop {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}
.trip-detail-headtext {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}
.trip-detail-headtop .trip-edit-btn {
  border: 1px solid var(--trp-border);
  background: var(--trp-bg-card);
  color: var(--teal-deep);
}
.trip-detail-headtop .trip-edit-btn:hover { background: var(--teal-soft); border-color: var(--teal); }
```

- [ ] **Step 5: Wire both entry points**

In `frontend/src/pages/trips/TripDetailPage.tsx`:

Replace the existing `MapRouteIcon`-only import line and add the dialog import:

```tsx
import { EditTripDialog } from './components/EditTripDialog'
import { MapRouteIcon, PencilIcon } from './components/TripFormIcons'
```

Add the state next to `dateError` (`:32`):

```tsx
  const [editOpen, setEditOpen] = useState(false)
```

Replace the desktop `<header className="trip-topbar">` block (`:106-117`) with:

```tsx
        <header className="trip-topbar">
          <span className="trip-topbar-name"><MapRouteIcon className="trip-topbar-ic" /> {trip?.name ?? '…'}</span>
          {trip && (
            <span className="trip-topbar-meta">
              {trip.destination && <>{trip.destination} · </>}
              <TripDateEditor trip={trip} overrideDate={overrideDate} locked={currentDay} onError={setDateError} />
              {trip.dayCount != null && <> · {trip.dayCount} วัน</>}
              <DailyToggle trip={trip} onError={setDateError} />
            </span>
          )}
          {dateError && <span className="trip-topbar-error">{dateError}</span>}
          {trip && (
            <button
              type="button"
              className="trip-edit-btn"
              aria-label="แก้ไขทริป"
              title="แก้ไขทริป"
              onClick={() => setEditOpen(true)}
            >
              <PencilIcon />
            </button>
          )}
        </header>
```

Replace the mobile `<header className="trip-detail-header">` block (`:190-201`) with:

```tsx
      <header className="trip-detail-header">
        <div className="trip-detail-headtop">
          <div className="trip-detail-headtext">
            <div className="trip-detail-name">{trip?.name ?? '…'}</div>
            {trip && (
              <div className="trip-detail-meta">
                {trip.destination && <>{trip.destination} · </>}
                <TripDateEditor trip={trip} overrideDate={overrideDate} locked={currentDay} onError={setDateError} />
                {trip.dayCount != null && <> · {trip.dayCount} วัน</>}
                <DailyToggle trip={trip} onError={setDateError} />
              </div>
            )}
          </div>
          {trip && (
            <button
              type="button"
              className="trip-edit-btn"
              aria-label="แก้ไขทริป"
              title="แก้ไขทริป"
              onClick={() => setEditOpen(true)}
            >
              <PencilIcon />
            </button>
          )}
        </div>
        {dateError && <p className="trips-field-error">{dateError}</p>}
      </header>
```

Mount the dialog in **both** branches. Desktop — after the existing `{editingPlace && (...)}` block at `:180-182`:

```tsx
        {trip && editOpen && (
          <EditTripDialog trip={trip} onClose={() => setEditOpen(false)} />
        )}
```

Mobile — after the existing `{editingPlace && (...)}` block at `:258-260`, before `{addStopContext && (...)}`:

```tsx
      {trip && editOpen && (
        <EditTripDialog trip={trip} onClose={() => setEditOpen(false)} />
      )}
```

- [ ] **Step 6: Typecheck, build, run the suite**

```bash
cd frontend
npx tsc -b && npm run build && npm run test -- --run
```
Expected: PASS.

- [ ] **Step 7: Verify interactively before committing**

Run the app, open a trip, and check: the pencil appears in the mobile header (top-right, aligned with the trip name) and in the desktop top-bar (far right, vertically centred against the baseline-aligned text); it opens a teal dialog whose header badge is a pencil; the five fields are pre-filled from the trip; `TripDateEditor` is still inline in the header beside it. Rename the trip -> save -> the header name updates. Open again -> change nothing -> save -> the dialog closes and the network tab shows **no** `PUT /api/trips/...`.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/pages/trips/components/EditTripDialog.tsx \
        frontend/src/pages/trips/components/TripFormIcons.tsx \
        frontend/src/pages/trips/TripDetailPage.tsx \
        frontend/src/pages/trips/TripDetailPage.css \
        frontend/src/pages/trips/TripsPage.css
git commit -m "feat(trips): EditTripDialog opened by a pencil button in both trip headers (#50)"
```

---

## Task 5: Frontend — the shrink confirm, and the day count disabled while unknown (ADR-138/139/140)

**Files:**
- Modify: `frontend/src/shared/api/api.ts:1363`
- Modify: `frontend/src/pages/trips/components/EditTripDialog.tsx`
- Modify: `frontend/src/pages/trips/components/TripFormIcons.tsx` (append `ClockIcon`)
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx` (pass `days` + `places`)
- Modify: `frontend/src/pages/trips/trips-tokens.css` (append)
- Modify: `frontend/src/pages/trips/TripsPage.css` (append)

**Interfaces:**
- Consumes: `shrinkLoss`, `capNames`, `type ShrinkLoss` from `../lib/tripEdit`; `useConfirm` from `../../../shared/hooks/useConfirm`; the backend `AllowStopLoss` from Task 1.
- Produces: `EditTripDialog({trip, days, places, onClose}: {trip: TripDto; days: ItineraryDayDto[]; places: TripPlaceDto[]; onClose: () => void})`.

**Context the implementer needs:** `TripDetailPage` already calls `useDayRoute(tripId)` unconditionally, which fires `getItinerary`; `ItineraryDayDto` carries the full per-day stops array including `isVisited`, and `listTripPlaces` is loaded alongside for the names. **Never fire `getItinerary` to price a confirm** — a refetch re-bills the Google Routes API and re-fetches Weather (ADR-139/042), and a query without the detail page's `lat`/`lng` would get a different cache key anyway. While the count is unknown, **only** the day-count control is disabled; name, destination, start date and travel mode stay editable throughout. Never default an unknown count to zero — that is silent destruction wearing a confirm's clothes.

**Reference:** the approved mock, panel **C2** (count-unknown) and panel **D** (the confirm).

- [ ] **Step 1: Let the RTK arg carry the flag**

In `frontend/src/shared/api/api.ts`, replace line 1363:

```ts
        updateTrip: build.mutation<TripDto, {id: string; name: string; destination?: string | null; startDate: string; dayCount: number; defaultTravelMode: TravelMode; allowStopLoss?: boolean}>({
```

Callers that omit it send no `allowStopLoss` key at all, and the server defaults it to `false`. `TripDateEditor` must stay one of those callers: it passes `dayCount` through unchanged, so its PUT is never a Shrink and the guard cannot fire on it (ADR-142).

- [ ] **Step 2: Add the clock icon**

Append to `frontend/src/pages/trips/components/TripFormIcons.tsx`:

```tsx
/** Clock — "still loading, the at-risk count is not known yet" reason line. */
export function ClockIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <circle cx="12" cy="12" r="10" />
      <polyline points="12 6 12 12 15 14" />
    </svg>
  )
}
```

- [ ] **Step 3: Add the confirm-content CSS (global, not page-scoped)**

Append to `frontend/src/pages/trips/trips-tokens.css`:

```css
/* ============================================================
   Content styling for the two destructive confirms EditTripDialog raises (issue #50).
   ConfirmProvider's Dialog is portaled to document.body with NO wrapper class, so these
   rules must be GLOBAL — the same reason the --review fallback at the top of this file
   is on :root. Only the message body is styled: the modal's own chrome is deliberately
   left as raw Syncfusion, because useConfirm is mounted app-wide via AppLayout and
   restyling it is out of scope for #50.
   ============================================================ */
.trip-confirm-loss {
  margin: 10px 0 0;
  padding: 11px 13px;
  border: 1px solid #fecaca;
  border-radius: 9px;
  background: #fef2f2;
  color: #0f172a;
  font-size: 13px;
  line-height: 1.65;
}
.trip-confirm-loss ul { margin: 6px 0 0; padding-left: 18px; }
.trip-confirm-loss li { margin: 2px 0; }
.trip-confirm-tag {
  display: inline-block;
  margin-left: 5px;
  padding: 0 7px;
  vertical-align: 1px;
  border-radius: 999px;
  background: #e2e8f0;
  color: #475569;
  font-size: 10.5px;
  font-weight: 700;
}
.trip-confirm-final {
  display: block;
  margin-top: 9px;
  color: #b91c1c;
  font-size: 12.5px;
  font-weight: 700;
}
```

- [ ] **Step 4: Add the disabled-stepper CSS**

Append to `frontend/src/pages/trips/TripsPage.css`, after the `.ctd-errbox` rules from Task 4:

```css
/* Disabled-with-a-reason treatment. Two controls use it, for THREE different reasons —
   each supplies its own copy (ADR-139 count-unknown, ADR-144 daily x2). */
.create-trip-dialog .ctd-stepper.is-disabled {
  border-color: #eef2f6;
  background: #f8fafc;
}
.create-trip-dialog .ctd-stepper.is-disabled .ctd-step-val { color: #cbd5e1; }
.create-trip-dialog .ctd-why {
  display: flex;
  align-items: flex-start;
  gap: 5px;
  margin-top: 1px;
  color: var(--muted);
  font-size: 11.5px;
  line-height: 1.45;
}
.create-trip-dialog .ctd-why svg {
  width: 12px;
  height: 12px;
  flex: none;
  margin-top: 2px;
  color: #cbd5e1;
}
```

- [ ] **Step 5: Extend the dialog**

In `EditTripDialog.tsx`:

Extend the imports:

```tsx
import {useUpdateTripMutation, type ItineraryDayDto, type TravelMode, type TripDto, type TripPlaceDto} from '../../../shared/api/api'
import {useConfirm} from '../../../shared/hooks/useConfirm'
import {capNames, draftFromTrip, isDraftDirty, normalizeDraft, shrinkLoss, type ShrinkLoss, type TripEditDraft} from '../lib/tripEdit'
import {AlertIcon, ArrowRightIcon, CarIcon, CheckIcon, ClockIcon, MapPinIcon, MinusIcon, PencilIcon, PlusIcon, TransitIcon, WalkIcon} from './TripFormIcons'
```

Add these module-level helpers below `MAX_DAYS`:

```tsx
/** "yyyy-MM-dd" -> Thai BE label, falling back to the raw value if it will not parse. */
function th(ymd: string): string {
  const d = ymdToDate(ymd)
  return d ? thaiDate(d) : ymd
}

/**
 * What the confirm says before a Shrink destroys stops (ADR-138): the day range, the stop
 * count, the place NAMES (capped for the 420px dialog), and a distinct tag on any stop
 * already marked มาแล้ว — that is recorded history, and a bare number hides it.
 */
function ShrinkLossMessage({loss}: {loss: ShrinkLoss}) {
  const {shown, moreCount} = capNames(loss.stops, 5)
  const range = loss.dayFrom === loss.dayTo ? `วันที่ ${loss.dayFrom}` : `วันที่ ${loss.dayFrom}–${loss.dayTo}`
  const dates = loss.dateFrom === loss.dateTo ? th(loss.dateFrom) : `${th(loss.dateFrom)} – ${th(loss.dateTo)}`
  return (
    <>
      {range} ({dates}) จะถูกลบ พร้อม <b>จุดแวะ {loss.stops.length} จุด</b> บนวันนั้น
      <div className="trip-confirm-loss">
        จุดแวะที่จะหายไป
        <ul>
          {shown.map((s, i) => (
            <li key={i}>
              {s.name}
              {s.isVisited && <span className="trip-confirm-tag">มาแล้ว</span>}
            </li>
          ))}
          {moreCount > 0 && <li>…และอีก {moreCount} แห่ง</li>}
        </ul>
        <span className="trip-confirm-final">ลบแล้วกู้คืนไม่ได้</span>
      </div>
    </>
  )
}
```

Change the component signature:

```tsx
export function EditTripDialog({
  trip,
  days,
  places,
  onClose,
}: {
  trip: TripDto
  /** The itinerary already in the RTK cache. `[]` means "not loaded" — a trip always has >=1 day. */
  days: ItineraryDayDto[]
  places: TripPlaceDto[]
  onClose: () => void
}) {
```

and add these right after `const [updateTrip, {isLoading}] = useUpdateTripMutation()`:

```tsx
  const {confirm} = useConfirm()

  // ADR-139: the day-count control is live ONLY where the itinerary is already cached, and
  // is DISABLED — with its reason shown — while the count cannot be priced. This covers the
  // in-flight window, the refire when geolocation resolves, and an outright fetch failure.
  // Never default the unknown count to zero: that is the failure mode this whole guard exists
  // to prevent. The other four fields stay editable throughout.
  const daysKnown = days.length > 0
  const placeNameById = useMemo(
    () => Object.fromEntries(places.map((p) => [p.id, p.name])) as Record<string, string>,
    [places],
  )
```

Replace the day-count field block with:

```tsx
          <div className="ctd-field">
            <label className="ctd-label">
              จำนวนวัน {daysKnown && <span className="ctd-req">*</span>}
            </label>
            <div className={`ctd-stepper${daysKnown ? '' : ' is-disabled'}`}>
              <button
                type="button"
                className="ctd-step"
                aria-label="ลดจำนวนวัน"
                disabled={!daysKnown || draft.dayCount <= MIN_DAYS}
                onClick={() => set('dayCount', Math.max(MIN_DAYS, draft.dayCount - 1))}
              >
                <MinusIcon />
              </button>
              <span className="ctd-step-val" aria-live="polite">
                {draft.dayCount}
              </span>
              <button
                type="button"
                className="ctd-step"
                aria-label="เพิ่มจำนวนวัน"
                disabled={!daysKnown || draft.dayCount >= MAX_DAYS}
                onClick={() => set('dayCount', Math.min(MAX_DAYS, draft.dayCount + 1))}
              >
                <PlusIcon />
              </button>
            </div>
            {!daysKnown && (
              <span className="ctd-why">
                <ClockIcon />
                กำลังโหลดแผนเที่ยว — ยังนับจุดแวะที่จะหายไม่ได้
              </span>
            )}
          </div>
```

Replace everything in `save` from the dirty-diff early return onward:

```tsx
    if (!isDraftDirty(d, trip)) {
      onClose()
      return
    }

    // ADR-138: exactly ONE confirm against the NET change, fired on save rather than on each
    // tap of the minus button — 5 -> 3 is one decision, not two. It fires only when the dropped
    // days really hold stops; a shrink over empty days is an ordinary edit. Priced entirely
    // from the cache this dialog was already handed (ADR-139) — nothing is fetched for it.
    let allowStopLoss = false
    const loss = shrinkLoss(days, placeNameById, d.dayCount)
    if (loss) {
      const ok = await confirm({
        title: `ลดจำนวนวันจาก ${days.length} เหลือ ${d.dayCount}?`,
        message: <ShrinkLossMessage loss={loss} />,
        confirmText: 'ลบวันและจุดแวะ',
        destructive: true,
      })
      if (!ok) return
      allowStopLoss = true
    }

    try {
      await updateTrip({
        id: trip.id,
        name: d.name,
        destination: d.destination || null,
        startDate: d.startDate,
        dayCount: d.dayCount,
        defaultTravelMode: d.defaultTravelMode,
        // Only ever true immediately after the user confirmed the loss above (ADR-140).
        allowStopLoss,
      }).unwrap()
      onClose()
    } catch (e) {
      setSaveError(getErrorMessage(e))
    }
```

- [ ] **Step 6: Feed the dialog its cache**

In `TripDetailPage.tsx`, update **both** mount sites:

```tsx
          <EditTripDialog
            trip={trip}
            days={dayRoute.days}
            places={places ?? []}
            onClose={() => setEditOpen(false)}
          />
```

- [ ] **Step 7: Typecheck, build, run the suite**

```bash
cd frontend
npx tsc -b && npm run build && npm run test -- --run
```
Expected: PASS.

- [ ] **Step 8: Verify interactively before committing**

On a trip whose **last** day holds at least one stop (mark one มาแล้ว first): open the dialog, step the day count down past that day, save. The confirm must appear **above** the edit dialog — if it renders behind it, see the fallback below — and must name the day range, the dates, the stop count and each place name, with a `มาแล้ว` tag on the visited one. Cancel -> nothing happens, dialog still open. Confirm -> the save succeeds and the itinerary loses those days. Then shrink over an **empty** trailing day: **no** confirm at all, saves straight through. Open the dialog while the itinerary is still loading (throttle the network): the stepper is greyed with the clock reason line and the other four fields still work.

**Fallback if the confirm renders behind the edit dialog:** Syncfusion assigns modals an increasing z-index as they open, so the later one should win. If it does not, add an explicit `zIndex` to `ConfirmProvider`'s `Dialog` `style` (`frontend/src/shared/components/ConfirmProvider.tsx:73`) — a stacking fix, which is **not** the visual restyle that is out of scope. Also check the confirm against `.itin-reorder-overlay` (`z-index: 1200`), which was never verified.

- [ ] **Step 9: Commit**

```bash
git add frontend/src/shared/api/api.ts \
        frontend/src/pages/trips/components/EditTripDialog.tsx \
        frontend/src/pages/trips/components/TripFormIcons.tsx \
        frontend/src/pages/trips/TripDetailPage.tsx \
        frontend/src/pages/trips/trips-tokens.css \
        frontend/src/pages/trips/TripsPage.css
git commit -m "feat(trips): confirm a stop-destroying day-count shrink before saving (#50)"
```

---

## Task 6: Frontend — daily trips (ADR-144)

**Files:**
- Modify: `frontend/src/pages/trips/components/EditTripDialog.tsx`
- Modify: `frontend/src/pages/trips/components/TripFormIcons.tsx` (append `InfoIcon`)
- Modify: `frontend/src/pages/trips/components/DailyToggle.tsx:16`
- Modify: `frontend/src/pages/trips/TripDetailPage.tsx` (pass `overrideDate`)
- Modify: `frontend/src/pages/trips/TripsPage.css` (append)

**Interfaces:**
- Produces: `EditTripDialog({trip, days, places, overrideDate, onClose})` — `overrideDate?: string` is the server-projected "today" `TripDetailPage` already computes at `:99-100`.

**Context the implementer needs:** two of the five fields cannot mean anything on a daily trip, for **different** reasons, and each gets its **own** copy — this is why the mock draws all three disabled cases side by side.

- `dayCount` is pinned at 1 **permanently**: `Trip.Reschedule` throws on `IsDaily && dayCount > 1` and `SetDaily` throws on `DayCount != 1`. That is *impossible*, not merely *unknown* — a different case from ADR-139's "cannot be priced yet".
- `startDate` is accepted by `Reschedule` but **displayed nowhere**: `dailyCard` has no date row, `TripDateEditor` is always `locked` on a daily trip and renders the server-projected `overrideDate`, and `GetItineraryHandler` projects the date to today calling the persisted value "the fallback". A start-date control here would save successfully and change nothing the user can see, anywhere.

So the field **displays today while disabled** — and the **draft keeps the persisted value**, so the PUT never moves it and the dirty-diff never trips on it. Both fields are shown disabled, never hidden: the dialog has one shape for every trip, and hiding them would delete the only place the constraint is ever explained.

**Reference:** the approved mock, panel **C1** and panel **F** (the cross-surface race).

- [ ] **Step 1: Add the info icon**

Append to `frontend/src/pages/trips/components/TripFormIcons.tsx`:

```tsx
/** Info circle — "this cannot be changed on this kind of trip" reason line. */
export function InfoIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <circle cx="12" cy="12" r="10" />
      <line x1="12" y1="16" x2="12" y2="12" />
      <line x1="12" y1="8" x2="12.01" y2="8" />
    </svg>
  )
}
```

- [ ] **Step 2: Add the disabled date-field CSS**

Append to `frontend/src/pages/trips/TripsPage.css`, after the `.ctd-why` rules:

```css
/* A disabled Syncfusion DatePicker must read as "deliberately locked", not as broken. */
.create-trip-dialog .sf-input-group.sf-disabled,
.create-trip-dialog .sf-input-group:has(.sf-input:disabled) {
  border-color: #eef2f6 !important;
  background: #f8fafc !important;
}
.create-trip-dialog .sf-input:disabled { color: #94a3b8; -webkit-text-fill-color: #94a3b8; }
```

- [ ] **Step 3: Add the daily branches to the dialog**

In `EditTripDialog.tsx`, extend the icon import with `InfoIcon`, add `overrideDate?: string` to the props, and add these derived values next to `daysKnown`:

```tsx
  // ── Daily trips (ADR-144) ────────────────────────────────────────────────────
  // Two fields cannot mean anything here, for DIFFERENT reasons, so each carries its own
  // copy. Both stay VISIBLE and disabled — the dialog has one shape for every trip, and
  // hiding them would delete the only place the constraint is ever explained.
  const isDaily = trip.isDaily
  const todayYmd = dateToYmd(new Date()) ?? draft.startDate
  // The persisted start date of a daily trip is displayed NOWHERE in the app (dailyCard has
  // no date row, TripDateEditor is always locked, GetItinerary projects the date to today),
  // so it is a fallback, not a value. DISPLAY today — but keep `draft.startDate` on the
  // persisted value so the save never moves it and the dirty-diff never trips on it.
  const displayStartYmd = isDaily ? (overrideDate?.slice(0, 10) ?? todayYmd) : draft.startDate
  const dayCountDisabled = isDaily || !daysKnown
  const dayCountValue = isDaily ? 1 : draft.dayCount
```

Change the end-date summary to follow the coerced values — the same reason `CreateTripDialog` coerces its own summary, or it misrepresents the trip:

```tsx
  const endLabel = useMemo(() => {
    const e = endDate(ymdToDate(displayStartYmd), dayCountValue)
    return e ? thaiDate(e) : null
  }, [displayStartYmd, dayCountValue])
```

and its JSX to `รวม <b>{dayCountValue} วัน</b>`.

Replace the start-date field with:

```tsx
          <div className="ctd-field">
            <label className="ctd-label">
              วันเริ่ม {!isDaily && <span className="ctd-req">*</span>}
            </label>
            <DatePicker
              value={ymdToDate(displayStartYmd)}
              format="dd MMM yyyy"
              disabled={isDaily}
              onChange={(e: DatePickerChangeEvent) => {
                const v = dateToYmd(e.value)
                if (v) set('startDate', v)
              }}
            />
            {isDaily && (
              <span className="ctd-why">
                <InfoIcon />
                ทริปประจำวันเริ่ม “วันนี้” เสมอ
              </span>
            )}
          </div>
```

In the day-count field, change only the three `disabled` expressions, the displayed value, the `*`, and the reason line — every other attribute stays exactly as Task 5 left it:

- label: `จำนวนวัน {!dayCountDisabled && <span className="ctd-req">*</span>}`
- wrapper: ``className={`ctd-stepper${dayCountDisabled ? ' is-disabled' : ''}`}``
- minus button: `disabled={dayCountDisabled || draft.dayCount <= MIN_DAYS}`
- value span: `{dayCountValue}`
- plus button: `disabled={dayCountDisabled || draft.dayCount >= MAX_DAYS}`
- reason line, replacing the `{!daysKnown && (...)}` block:

```tsx
            {isDaily ? (
              <span className="ctd-why">
                <InfoIcon />
                ทริปประจำวันเป็นวันเดียวเสมอ
              </span>
            ) : !daysKnown ? (
              <span className="ctd-why">
                <ClockIcon />
                กำลังโหลดแผนเที่ยว — ยังนับจุดแวะที่จะหายไม่ได้
              </span>
            ) : null}
```

Nothing else is needed for the cross-surface race. `EditTripDialog` is `modal` like its sibling, so `DailyToggle` sits behind the overlay and cannot be reached from the same tab; a flip from another tab, another device or MCP lands on `Trip.Reschedule`'s domain guard, which the ADR-141 error path already routes into `.ctd-errbox` with the dialog left open. That is mock panel F, and it works with no new code.

- [ ] **Step 4: Pass `overrideDate` from the page**

In `TripDetailPage.tsx`, both mount sites:

```tsx
          <EditTripDialog
            trip={trip}
            days={dayRoute.days}
            places={places ?? []}
            overrideDate={overrideDate}
            onClose={() => setEditOpen(false)}
          />
```

- [ ] **Step 5: Make `DailyToggle` name the Shrink's cost**

In `frontend/src/pages/trips/components/DailyToggle.tsx`, replace `blockedMsg` (`:16`):

```tsx
  // ADR-133 keeps this refusal NON-DESTRUCTIVE — the switch never performs the Shrink. But
  // "ลบวันอื่น" IS a Shrink, the one irreversible destruction in MenuNest, so the message now
  // names what it costs and points at the surface that does it behind a confirm (ADR-144).
  // Built from trip.dayCount, already on TripDto — no new prop, no itinerary subscription.
  const blockedMsg =
    `ทริปประจำวันต้องเป็นวันเดียว — ทริปนี้มี ${trip.dayCount} วัน ` +
    `ลดเหลือ 1 วันได้ที่ปุ่ม “แก้ไข” (จุดแวะบนวันที่ถูกลบจะหายไปด้วย)`
```

Nothing else in the file changes: it still refuses, still renders clickable rather than `disabled` so touch users get the reason on tap, and still uses the same string for both the error line and the `title`.

- [ ] **Step 6: Typecheck, build, run the suite**

```bash
cd frontend
npx tsc -b && npm run build && npm run test -- --run
```
Expected: PASS.

- [ ] **Step 7: Verify interactively before committing**

On a **daily** trip: the dialog shows all five fields; วันเริ่ม is greyed, reads **today**, and carries `ทริปประจำวันเริ่ม "วันนี้" เสมอ`; จำนวนวัน is greyed at 1 with `ทริปประจำวันเป็นวันเดียวเสมอ` — different copy from the count-unknown case; the summary pill reads 1 วัน. Rename it -> save -> succeeds, and the trip's start date is **unchanged** in the API response. On a **multi-day** trip: tap the daily switch -> the error line names the current day count and points at แก้ไข.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/pages/trips/components/EditTripDialog.tsx \
        frontend/src/pages/trips/components/TripFormIcons.tsx \
        frontend/src/pages/trips/components/DailyToggle.tsx \
        frontend/src/pages/trips/TripDetailPage.tsx \
        frontend/src/pages/trips/TripsPage.css
git commit -m "feat(trips): daily trips disable date and day count with their own reasons (#50)"
```

---

## Task 7: Frontend — delete the trip from the dialog footer (ADR-143)

**Files:**
- Modify: `frontend/src/pages/trips/components/EditTripDialog.tsx`
- Modify: `frontend/src/pages/trips/components/TripFormIcons.tsx` (append `TrashIcon`)
- Modify: `frontend/src/pages/trips/TripsPage.css` (append)

**Interfaces:**
- Consumes: `useDeleteTripMutation` from `../../../shared/api/api` (it exists and has **zero** call sites in `frontend/src` today); `totalStops` from `../lib/tripEdit`; `useNavigate` from `react-router-dom`.
- Produces: nothing new for later tasks.

**Context the implementer needs:** `DeleteTripHandler` is a **pure soft delete** — `trip.SoftDelete()` sets `DeletedAt` and nothing else. Days, Stops, TripPlaces, checklist entries and Place profiles all survive untouched in the database. **The copy must therefore not claim the stops are deleted** — say they *disappear from* the app, never that they are *erased*. What the user cannot predict from the words "ลบทริป" is that this trip's places also vanish from **ไปไหนดี**, because `deleteTrip` invalidates `MyPlaces` and `ListMyPlacesHandler` filters `t.DeletedAt == null` — hence its own line.

On success **navigate to `/trips`**: staying put hits `TripDetailPage:86-92`'s not-found guard, which renders *"ไม่พบทริปนี้ — อาจถูกลบ หรือลิงก์ไม่ถูกต้อง"* and reads as an error for something the user just asked for. No toast: there is no shared toast system, and the trip's absence from the list is the feedback.

**Reference:** the approved mock, panel **B** (the split footer) and panel **E** (the confirm).

- [ ] **Step 1: Add the trash icon**

Append to `frontend/src/pages/trips/components/TripFormIcons.tsx`:

```tsx
/** Trash — the edit dialog's ลบทริป footer action. */
export function TrashIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
      <path d="M10 11v6M14 11v6" />
      <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
    </svg>
  )
}
```

- [ ] **Step 2: Add the split-footer and danger-button CSS**

Append to `frontend/src/pages/trips/TripsPage.css`:

```css
/* EditTripDialog's footer SPLITS: ลบทริป hard left, ยกเลิก/บันทึก right (approved mock,
   panel B). Applied as a MODIFIER and never by editing .ctd-actions itself — that rule is
   shared with CreateTripDialog, whose two-button footer must stay flex-end. */
.create-trip-dialog .ctd-actions-split { justify-content: space-between; }
.create-trip-dialog .ctd-actions-r { display: flex; gap: 10px; }

/* Deliberately reads as DESTRUCTIVE, breaking the muted .se-delete precedent (ADR-143).
   .se-delete is muted because removing a stop or a place is recoverable — you can add it
   back. Deleting a trip is not, in any way the user can act on, and a muted control that
   raises a red confirm is an incoherence. Do NOT "fix" this to match .se-delete. */
.create-trip-dialog .ctd-btn-danger {
  padding: 11px 16px;
  border-color: #fecaca;
  background: var(--surface);
  color: var(--error);
}
.create-trip-dialog .ctd-btn-danger:hover:not(:disabled) { border-color: #f87171; background: #fef2f2; }
.create-trip-dialog .ctd-btn-danger:disabled { opacity: 0.6; cursor: default; }
```

- [ ] **Step 3: Add the delete action**

In `EditTripDialog.tsx`, extend the imports:

```tsx
import {useNavigate} from 'react-router-dom'
import {useDeleteTripMutation, useUpdateTripMutation, type ItineraryDayDto, type TravelMode, type TripDto, type TripPlaceDto} from '../../../shared/api/api'
import {capNames, draftFromTrip, isDraftDirty, normalizeDraft, shrinkLoss, totalStops, type ShrinkLoss, type TripEditDraft} from '../lib/tripEdit'
```
and add `TrashIcon` to the `./TripFormIcons` import list.

Add next to the other hooks:

```tsx
  const [deleteTrip, {isLoading: isDeleting}] = useDeleteTripMutation()
  const navigate = useNavigate()
```

Add the handler after `save`:

```tsx
  const handleDelete = async () => {
    setSaveError(null)
    // "N วัน · M จุดแวะ" identifies the trip at a glance and is free: ADR-139 already
    // requires this dialog to open where the itinerary is cached. Omitted while unknown
    // rather than guessed — the name alone still identifies it.
    const ok = await confirm({
      title: 'ลบทริปนี้?',
      message: (
        <>
          <b>“{trip.name}”</b>
          {daysKnown && (
            <>
              {' '}
              · {days.length} วัน · {totalStops(days)} จุดแวะ
            </>
          )}
          <div className="trip-confirm-loss">
            {/* DeleteTripHandler is a pure soft delete — the stops are NOT erased, so the copy
                must not say they are. What is true, and unguessable from "ลบทริป", is that
                this trip's places also leave Discover. */}
            สถานที่ในทริปนี้จะหายจาก <b>ไปไหนดี</b> ด้วย
            <span className="trip-confirm-final">ลบแล้วกู้คืนไม่ได้</span>
          </div>
        </>
      ),
      confirmText: 'ลบทริป',
      destructive: true,
    })
    if (!ok) return
    try {
      await deleteTrip(trip.id).unwrap()
      // Leave immediately: staying put hits TripDetailPage's not-found guard, which reads as
      // an error for something the user just asked for. No toast — the app has no shared toast
      // system, and the trip's absence from /trips is the feedback (ADR-143).
      navigate('/trips')
    } catch (e) {
      setSaveError(getErrorMessage(e))
    }
  }
```

Replace the whole footer:

```tsx
        <div className="ctd-actions ctd-actions-split">
          <button
            type="button"
            className="ctd-btn ctd-btn-danger"
            disabled={isLoading || isDeleting}
            onClick={() => void handleDelete()}
          >
            <TrashIcon /> ลบทริป
          </button>
          <div className="ctd-actions-r">
            <button type="button" className="ctd-btn ctd-btn-ghost" onClick={onClose}>
              ยกเลิก
            </button>
            <button type="submit" className="ctd-btn ctd-btn-primary" disabled={isLoading || isDeleting}>
              {isLoading ? (
                '…'
              ) : (
                <>
                  <CheckIcon /> บันทึก
                </>
              )}
            </button>
          </div>
        </div>
```

Unsaved edits in the form are simply discarded — delete supersedes them, and cancel already does not warn on dirty state.

- [ ] **Step 4: Typecheck, build, run the suite**

```bash
cd frontend
npx tsc -b && npm run build && npm run test -- --run
```
Expected: PASS.

- [ ] **Step 5: Verify interactively before committing**

Create a throwaway trip with a couple of places and stops. Open แก้ไข: `ลบทริป` sits hard **left** in a red outline, `ยกเลิก` / `บันทึก` right — and `CreateTripDialog`'s own footer is **unchanged** (its two buttons still sit together on the right; check it). Tap ลบทริป -> the confirm names the trip, `N วัน · M จุดแวะ` and the ไปไหนดี line, and never says the stops are deleted. Cancel -> nothing happens. Confirm -> you land on `/trips`, the trip is gone from the list, and the place is gone from ไปไหนดี.

**Watch for a flash of `ไม่พบทริปนี้`** between the `TripDetail` invalidation and the navigation. If it flashes, call `navigate('/trips')` **before** awaiting `deleteTrip(...).unwrap()` and drop the local error branch for delete — a delete that fails after navigation is far less bad than a successful one that looks like an error.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/trips/components/EditTripDialog.tsx \
        frontend/src/pages/trips/components/TripFormIcons.tsx \
        frontend/src/pages/trips/TripsPage.css
git commit -m "feat(trips): delete a trip from the edit dialog footer (#50)"
```

---

## Task 8: Frontend — both start-date pickers stop offering a Backdate (ADR-146)

**Files:**
- Modify: `frontend/src/pages/trips/components/EditTripDialog.tsx`
- Modify: `frontend/src/pages/trips/components/TripDateEditor.tsx:1-2,24-26,99-110`

**Interfaces:**
- Consumes: the server guard from Task 2.
- Produces: nothing.

**Context the implementer needs:** it is **`minDate`, not `min`** — inherited from `CalendarBaseProps` — and it gates selectability (`datepicker.js:41` returns `false` from the selectable check for out-of-range dates). **Leave `strictMode` at its default `false`**: enabled, it "auto-corrects" invalid values, which on a past trip would rewrite the displayed start date. `minDate` on a Syncfusion picker is unproven in this repo — every existing `min=` is a plain number input — so Step 4 is not optional.

`TripDateEditor` gets **one prop and nothing else**. It must **not** be given `allowStopLoss`: it passes `dayCount` through unchanged, so its PUT is never a Shrink.

**Reference:** the approved mock, panel **C3** — a past-dated trip has **nothing** disabled; `minDate` does all the work, and the two red-flagged checks are drawn there.

- [ ] **Step 1: Bound the dialog's picker**

In `EditTripDialog.tsx`, add next to `todayYmd`:

```tsx
  // ADR-146: a Backdate is refused server-side, so never offer one. Memoised because a fresh
  // Date object every render would churn the picker's prop identity.
  const minDate = useMemo(() => {
    const d = new Date()
    d.setHours(0, 0, 0, 0)
    return d
  }, [])
```

and add `minDate={minDate}` to the `DatePicker` in the start-date field. Do not add `strictMode`.

- [ ] **Step 2: Bound the inline header picker**

In `frontend/src/pages/trips/components/TripDateEditor.tsx`, add `useMemo` to the `react` import, then add above the `return`:

```tsx
  // ADR-146 amends ADR-142: this component writes Trip.StartDate too, so it must not offer a
  // pick the server always refuses. One prop — the existing onError + optimistic-revert path
  // still handles a refusal that slips through. strictMode stays at its default false; enabled,
  // it would auto-correct a past trip's out-of-range value and rewrite the displayed date.
  const minDate = useMemo(() => {
    const d = new Date()
    d.setHours(0, 0, 0, 0)
    return d
  }, [])
```

and add `minDate={minDate}` to its `DatePicker` (`:101-110`).

Also extend the component's doc comment (`:24-26`) so the next reader is not told something that is no longer complete:

```tsx
 * unchanged, so no itinerary days are dropped (shrinking is out of scope). The picker is
 * bounded by minDate so it cannot offer a Backdate the server would refuse (ADR-146). The
```

- [ ] **Step 3: Typecheck, build, run the suite**

```bash
cd frontend
npx tsc -b && npm run build && npm run test -- --run
```
Expected: PASS.

- [ ] **Step 4: Verify interactively — this task ships blind otherwise**

Nothing automated can catch any of these. Seed a trip whose start date is **already in the past** (create it, then move it back with MCP `update_trip` against an environment that does not yet have Task 2's guard, or set it directly in the DB):

1. Open both pickers. Past days must be greyed and unselectable, in the dialog **and** in the header.
2. **The past trip's own out-of-range value must still DISPLAY** in the field rather than blanking.
3. **It must not fire `onChange`.** In the dialog that would make the dirty-diff think the user edited the date and silently move the trip to today — a forward move the server happily accepts. In the header it would fire an immediate PUT. Open the dialog on a past trip, change **nothing**, hit บันทึก: there must be **no** `PUT /api/trips/...` in the network tab.
4. Everything else on a past trip still edits normally — name, destination, day count, travel mode — and the date can still move **forward**.

**If check 2 or 3 fails, drop `minDate` from that picker** and fall back to the server guard plus the dialog's local error line. Record which picker and why in the commit body.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/trips/components/EditTripDialog.tsx \
        frontend/src/pages/trips/components/TripDateEditor.tsx
git commit -m "feat(trips): both start-date pickers stop offering a Backdate (#50)"
```

---

## Task 9: Verification against the approved mock, then merge

**Files:** none changed unless a check fails.

This task exists because **the review gates are blind to visual fidelity.** SDD per-task review, whole-branch review and `/scrutinize` verify behaviour and spec-compliance; none of them renders the UI or compares it to the mock. #46 shipped flat through every gate straight to prod. Prod deploys on push to `main`, so everything below happens **before** the push.

- [ ] **Step 1: Fetch the approved mock**

```
DesignSync get_file
  projectId: 8d8d4c81-41c1-4e0a-a0b7-370b39dfbe70
  path: screens/issue-50-trip-edit-delete.html
```

- [ ] **Step 2: Diff the built UI against it, panel by panel**

| Panel | What must match |
|---|---|
| A | Pencil in **both** headers — light-on-dark 30px/9px-radius on the desktop `.trip-topbar` (far right, vertically centred), teal-on-white top-right on the mobile header. `TripDateEditor` still inline beside it. |
| B | Five fields, **no** daily switch, 22px teal dialog, 46px pencil badge, `44px 1fr 44px` stepper, teal-soft summary pill. Footer **split**: `ลบทริป` hard left in red outline, `ยกเลิก`/`บันทึก` right. |
| C1 | Daily: วันเริ่ม greyed showing **today**, จำนวนวัน greyed at 1 — **two different reason strings**. |
| C2 | Count unknown: **only** จำนวนวัน greyed, its own clock-icon reason, other three fields live. |
| C3 | Past-dated: **nothing** disabled, calendar greys past days. |
| D | Shrink confirm: day range, dates, stop count, capped place names, `มาแล้ว` tag, `ลบแล้วกู้คืนไม่ได้`. |
| E | Delete confirm: name, `N วัน · M จุดแวะ`, the ไปไหนดี line, and **no** claim that the stops are deleted. |
| F | Save failure: dialog stays open, boxed error inside it, backend message in **English**. |

Confirm the two confirms render as **raw Syncfusion** (8px corners, square buttons) — that is the approved decision, not a defect.

- [ ] **Step 3: Regression-check what #50 must NOT have changed**

- `TripsPage` — `git diff main/main -- frontend/src/pages/trips/TripsPage.tsx` must be **empty**, and `data-testid="trip-card"` untouched.
- `CreateTripDialog` — open it: its footer still has both buttons together on the right, and its own start-date picker is still **unbounded** (create deliberately still allows a past date and a daily start date, ADR-144/146).
- `TripDateEditor` — still commits on the first pick, still locked on a daily trip, still reverts on failure.

- [ ] **Step 4: Run every gate one more time**

```bash
cd backend && dotnet build && dotnet test
cd ../frontend && npx tsc -b && npm run build && npm run test -- --run
```

- [ ] **Step 5: Push**

The repo is worked by concurrent sessions and the single remote is named `main`, not `origin`:

```bash
git fetch main
git rebase main/main          # resolve, then re-run the gates in Step 4 if anything moved
git push main HEAD:main
```

- [ ] **Step 6: Close the issue with the docs commit**

The decision-map tickets, the map and ADRs 138–146 are already committed. Commit this plan and close the issue:

```bash
git add docs/superpowers/plans/2026-08-01-trip-crud-edit-delete.md
git commit -m "docs(trips): implementation plan for trip edit and delete (closes #50)"
git push main HEAD:main
```

- [ ] **Step 7: Verify on prod**

There is **no EF migration** for #50 — both backend changes are Application-layer only, so CLAUDE.md's manual-migration ritual does not apply. After the deploy completes, on prod: rename a trip; shrink a trip that has stops (confirm fires, save succeeds); shrink one that does not (no confirm); and delete a throwaway trip (lands on `/trips`, its place is gone from ไปไหนดี).

---

## Out of scope — do not add these

Recorded on the decision map; a reviewer asking for any of them is asking for a different issue.

- Restoring a deleted trip — no undo toast, no trash bin, no restore endpoint.
- Moving the daily on/off toggle into the edit surface.
- Per-trip day or stop counts on the trips-list API.
- Editing or deleting a trip from the trips-list card, sorting/search on that list, multi-select, or any list management mode. **`TripsPage` is not touched at all.**
- Normalising the four Thai `DomainException` messages in Trips, or any frontend translation layer / ProblemDetails error-code contract.
- Disabling `CreateTripDialog`'s start-date field for a daily trip, or adding a past-date bound to it.
- Restyling the shared `useConfirm` modal, globally or as a Trips-only variant.
- Duplicating a trip — the "C" of CRUD. Create already exists in full; a duplicate is a new feature with its own destination.
- Place and Stop CRUD — already complete at both layers.
