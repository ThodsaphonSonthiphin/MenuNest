# Trip Stop "Visited" (มาแล้ว) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-Stop "มาแล้ว" (Visited) check box so a Trip owner can tick off stops they have reached — a display-only marker that never touches the Smart Schedule.

**Architecture:** One persisted boolean `IsVisited` on the `Stop` entity, written through the *existing* `PATCH /api/trips/{id}/stops/{stopId}` endpoint (nullable field, consumed only when present), read back on `StopDto`. The SPA renders a leading checkbox + de-emphasised row on `ItineraryStopCard`, and toggles it via a dedicated non-invalidating RTK Query mutation that optimistically patches the itinerary cache (no refetch → no Google Routes/Weather re-bill).

**Tech Stack:** .NET 10 (Domain / Application / Infrastructure / WebApi, EF Core, Mediator, xUnit + FluentAssertions + Moq), React + TypeScript + Redux Toolkit Query + Vite, Syncfusion UI, teal Trips design tokens.

**Design source:** [spec](../specs/2026-07-12-trip-stop-visited-design.md) · ADR [039](../../adr/039-stop-visited-display-marker.md)/[040](../../adr/040-visited-presentation.md)/[041](../../adr/041-visited-write-path-and-scope.md)/[042](../../adr/042-visited-non-invalidating-optimistic-write.md) · mock [`trip-stop-visited-mock.html`](../../mocks/trip-stop-visited-mock.html) · glossary term **Visited** in [`CONTEXT.md`](../../../CONTEXT.md).

## Global Constraints

- **Issue reference on every commit (#24).** Use conventional-commit style `type(scope): summary`. Intermediate commits end with `(#24)`; only the **final** commit (Task 6) uses `(closes #24)` — the user commits straight to `main`, so an earlier `closes` would shut the issue prematurely.
- **Pre-commit hook runs the FULL suite (~40s):** backend `dotnet build` + `dotnet test` (Release) and frontend `tsc -b` + `npm run build`. It must be green. Never `--no-verify`.
- **Stage narrowly:** always `git add <explicit paths>`; never `git add -A`/`.`. Never stage `daily-state.md` or `AGENTS.md`.
- **Migrations are applied to prod BY HAND** (Task 6) — neither the app nor CD runs `Database.Migrate()`.
- **Display-only (ADR-039):** `IsVisited` must never enter `computeSchedule` in `useSchedule.ts`; timing flags and weather are never suppressed on a visited card (ADR-040 §3).
- **Icons:** new UI glyphs are custom inline stroke SVGs in the FlagIcons/NavIcon style — **never emoji**.
- **Scope:** SPA only; the `update_stop` MCP tool is a compile-time consumer but does not expose Visited (ADR-041).

---

## File Structure

**Backend**
- `backend/src/MenuNest.Domain/Entities/Stop.cs` — add `IsVisited` + `SetVisited` (Task 1)
- `backend/src/MenuNest.Infrastructure/Persistence/Configurations/StopConfiguration.cs` — column default (Task 2)
- `backend/src/MenuNest.Infrastructure/Persistence/Migrations/*_AddStopIsVisited.cs` — generated (Task 2)
- `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs` — `StopDto.IsVisited` (Task 3)
- `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs` — pass `s.IsVisited` (Task 3)
- `backend/src/MenuNest.Application/UseCases/Trips/AddStop/AddStopHandler.cs` — pass `false` (Task 3)
- `backend/src/MenuNest.Application/UseCases/Trips/UpdateStop/UpdateStopCommand.cs` — `bool? IsVisited = null` (Task 3)
- `backend/src/MenuNest.Application/UseCases/Trips/UpdateStop/UpdateStopHandler.cs` — apply when non-null (Task 3)
- `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` — `UpdateStopBody` + command construction (Task 3)

**Frontend**
- `frontend/src/shared/api/api.ts` — `StopDto` interface + `setStopVisited` mutation + hook export (Task 4)
- `frontend/src/pages/trips/hooks/useSchedule.test.ts` — fixture gains `isVisited` (Task 4)
- `frontend/src/pages/trips/components/FlagIcons.tsx` — `CheckIcon` (Task 5)
- `frontend/src/pages/trips/components/ItineraryStopCard.tsx` — checkbox + visited class + chip (Task 5)
- `frontend/src/pages/trips/components/ItineraryTab.tsx` — wiring + day rollup (Task 5)
- `frontend/src/pages/trips/trips-tokens.css` — tokens + rules (Task 5)

**Tests**
- `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/StopTests.cs` — SetVisited (Task 1)
- `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateStopHandlerTests.cs` — new (Task 3)

---

### Task 1: Domain — `Stop.IsVisited` + `SetVisited`

**Files:**
- Modify: `backend/src/MenuNest.Domain/Entities/Stop.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/Domain/StopTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `Stop.IsVisited` (get; private set; bool, defaults false) and `Stop.SetVisited(bool value)`.

- [ ] **Step 1: Write the failing test** — append to `StopTests.cs` (inside the `StopTests` class):

```csharp
    [Fact]
    public void SetVisited_toggles_flag_and_defaults_false()
    {
        var s = Stop.Create(Guid.NewGuid(), Guid.NewGuid(), 0, 60, TravelMode.Drive);
        s.IsVisited.Should().BeFalse();      // new stop is never visited
        s.SetVisited(true);
        s.IsVisited.Should().BeTrue();
        s.SetVisited(false);
        s.IsVisited.Should().BeFalse();
    }
```

- [ ] **Step 2: Run it to confirm it fails**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~StopTests.SetVisited_toggles_flag_and_defaults_false"`
Expected: FAIL — compile error, `Stop` has no `IsVisited` / `SetVisited`.

- [ ] **Step 3: Add the property** — in `Stop.cs`, after the `Notes` property (line 20):

```csharp
    public string? Notes { get; private set; }
    public bool IsVisited { get; private set; }
```

- [ ] **Step 4: Add the mutator** — in `Stop.cs`, after `SetTravelMode` (end of class):

```csharp
    public void SetVisited(bool value)
    {
        IsVisited = value;
        UpdatedAt = DateTime.UtcNow;
    }
```

(No change to `Create(...)` — `IsVisited` defaults to `false`.)

- [ ] **Step 5: Run the test to confirm it passes**

Run: `dotnet test --filter "FullyQualifiedName~StopTests.SetVisited_toggles_flag_and_defaults_false"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Domain/Entities/Stop.cs backend/tests/MenuNest.Application.UnitTests/Trips/Domain/StopTests.cs
git commit -m "feat(trips): add Stop.IsVisited display-only marker + SetVisited (#24)"
```

---

### Task 2: Persistence — column default + EF migration

**Files:**
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/StopConfiguration.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<timestamp>_AddStopIsVisited.cs` (generated)

**Interfaces:**
- Consumes: `Stop.IsVisited` (Task 1).
- Produces: a `Stops.IsVisited bit NOT NULL DEFAULT 0` column in the model + migration.

- [ ] **Step 1: Add the column default** — in `StopConfiguration.cs`, after the `Notes` line (line 19; the builder parameter is `b`):

```csharp
        b.Property(s => s.Notes).HasMaxLength(2000);
        b.Property(s => s.IsVisited).HasDefaultValue(false);
```

- [ ] **Step 2: Generate the migration**

Run:
```bash
cd backend
dotnet ef migrations add AddStopIsVisited \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Expected: a new `*_AddStopIsVisited.cs` under `src/MenuNest.Infrastructure/Persistence/Migrations/` whose `Up()` calls `migrationBuilder.AddColumn<bool>("IsVisited", "Stops", nullable: false, defaultValue: false)`.

- [ ] **Step 3: Verify the generated SQL (do NOT apply to any DB here)**

Run:
```bash
dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Expected: the output contains `ALTER TABLE [Stops] ADD [IsVisited] bit NOT NULL DEFAULT CAST(0 AS bit);` (backfills existing rows to not-visited). Prod application is Task 6.

- [ ] **Step 4: Build to confirm the model + migration compile**

Run: `dotnet build`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/Persistence/Configurations/StopConfiguration.cs backend/src/MenuNest.Infrastructure/Persistence/Migrations/
git commit -m "feat(trips): migration + EF config for Stops.IsVisited column (#24)"
```

---

### Task 3: Backend contract — read (`StopDto`) + write (`UpdateStop`)

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs:90`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/AddStop/AddStopHandler.cs:34`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateStop/UpdateStopCommand.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Trips/UpdateStop/UpdateStopHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/TripsController.cs` (`UpdateStopBody` + line 87)
- Test: `backend/tests/MenuNest.Application.UnitTests/Trips/UpdateStopHandlerTests.cs` (new)

**Interfaces:**
- Consumes: `Stop.IsVisited`, `Stop.SetVisited` (Task 1).
- Produces:
  - `StopDto(Guid Id, Guid TripPlaceId, int Sequence, int DwellMinutes, TravelMode TravelModeToReach, LegDto? LegToReach, bool IsVisited)`
  - `UpdateStopCommand(Guid TripId, Guid StopId, int? DwellMinutes, TravelMode? TravelModeToReach, bool? IsVisited = null)`
  - `UpdateStopBody(int? DwellMinutes, TravelMode? TravelModeToReach, bool? IsVisited)`

- [ ] **Step 1: Write the failing handler test** — create `UpdateStopHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.UpdateStop;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public class UpdateStopHandlerTests
{
    private static (Trip trip, Stop stop) Seed(HandlerTestFixture fx)
    {
        var trip = Trip.Create(fx.User.Id, "t", new DateOnly(2026, 11, 1), 1, TravelMode.Drive);
        fx.Db.Trips.Add(trip);
        var day = ItineraryDay.Create(trip.Id, new DateOnly(2026, 11, 1));
        fx.Db.ItineraryDays.Add(day);
        var place = TripPlace.Create(trip.Id, "A", 0, 0, PlaceCategory.See);
        fx.Db.TripPlaces.Add(place);
        var stop = Stop.Create(day.Id, place.Id, 0, 60, TravelMode.Drive);
        fx.Db.Stops.Add(stop);
        fx.Db.SaveChanges();
        return (trip, stop);
    }

    [Fact]
    public async Task IsVisited_true_marks_the_stop_visited()
    {
        using var fx = new HandlerTestFixture();
        var (trip, stop) = Seed(fx);

        await new UpdateStopHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new UpdateStopCommand(trip.Id, stop.Id, null, null, IsVisited: true), CancellationToken.None);

        (await fx.Db.Stops.FirstAsync(s => s.Id == stop.Id)).IsVisited.Should().BeTrue();
    }

    [Fact]
    public async Task IsVisited_false_clears_the_flag()
    {
        using var fx = new HandlerTestFixture();
        var (trip, stop) = Seed(fx);
        stop.SetVisited(true);
        await fx.Db.SaveChangesAsync();

        await new UpdateStopHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new UpdateStopCommand(trip.Id, stop.Id, null, null, IsVisited: false), CancellationToken.None);

        (await fx.Db.Stops.FirstAsync(s => s.Id == stop.Id)).IsVisited.Should().BeFalse();
    }

    [Fact]
    public async Task Null_IsVisited_leaves_flag_and_still_updates_dwell()
    {
        using var fx = new HandlerTestFixture();
        var (trip, stop) = Seed(fx);
        stop.SetVisited(true);
        await fx.Db.SaveChangesAsync();

        await new UpdateStopHandler(fx.Db, fx.UserProvisioner.Object)
            .Handle(new UpdateStopCommand(trip.Id, stop.Id, DwellMinutes: 90, null, IsVisited: null), CancellationToken.None);

        var reloaded = await fx.Db.Stops.FirstAsync(s => s.Id == stop.Id);
        reloaded.IsVisited.Should().BeTrue();   // untouched — no regression
        reloaded.DwellMinutes.Should().Be(90);
    }
}
```

- [ ] **Step 2: Run it to confirm it fails**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~UpdateStopHandlerTests"`
Expected: FAIL — `UpdateStopCommand` has no 5th parameter / `Stop.IsVisited` not read.

- [ ] **Step 3: Extend the command (defaulted trailing param)** — replace the record body in `UpdateStopCommand.cs`:

```csharp
public sealed record UpdateStopCommand(
    Guid TripId, Guid StopId, int? DwellMinutes, TravelMode? TravelModeToReach, bool? IsVisited = null)
    : ICommand<Unit>;
```

(The `= null` default keeps the existing MCP call site `TripTools.cs:140`, which passes 4 args, compiling unchanged — ADR-041.)

- [ ] **Step 4: Apply it in the handler** — in `UpdateStopHandler.cs`, after the `SetTravelMode` block (line 27):

```csharp
        if (c.TravelModeToReach.HasValue)
            stop.SetTravelMode(c.TravelModeToReach.Value);
        if (c.IsVisited.HasValue)
            stop.SetVisited(c.IsVisited.Value);
```

- [ ] **Step 5: Extend the read DTO** — in `TripDtos.cs`, replace the `StopDto` record:

```csharp
public sealed record StopDto(
    Guid Id, Guid TripPlaceId, int Sequence, int DwellMinutes,
    TravelMode TravelModeToReach, LegDto? LegToReach, bool IsVisited);
```

- [ ] **Step 6: Fix the two `StopDto` construction sites**

In `GetItineraryHandler.cs:90`:
```csharp
                stopDtos.Add(new StopDto(s.Id, s.TripPlaceId, s.Sequence, s.DwellMinutes, s.TravelModeToReach, leg, s.IsVisited));
```

In `AddStopHandler.cs:34`:
```csharp
        return new StopDto(stop.Id, stop.TripPlaceId, stop.Sequence, stop.DwellMinutes, stop.TravelModeToReach, null, stop.IsVisited);
```

- [ ] **Step 7: Extend the controller body + command construction** — in `TripsController.cs`, replace `UpdateStopBody` (line 125) and the `UpdateStop` action body (line 87):

```csharp
    [HttpPatch("api/trips/{id:guid}/stops/{stopId:guid}")]
    public async Task<IActionResult> UpdateStop(Guid id, Guid stopId, [FromBody] UpdateStopBody b, CancellationToken ct)
    { await _mediator.Send(new UpdateStopCommand(id, stopId, b.DwellMinutes, b.TravelModeToReach, b.IsVisited), ct); return NoContent(); }
```

```csharp
public sealed record UpdateStopBody(
    int? DwellMinutes, TravelMode? TravelModeToReach, bool? IsVisited);
```

- [ ] **Step 8: Run the whole backend suite (proves no regression at other call sites)**

Run: `dotnet build && dotnet test`
Expected: build succeeds across the whole solution (incl. `MenuNest.McpServer`), all tests green, including the three new `UpdateStopHandlerTests`.

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs backend/src/MenuNest.Application/UseCases/Trips/GetItinerary/GetItineraryHandler.cs backend/src/MenuNest.Application/UseCases/Trips/AddStop/AddStopHandler.cs backend/src/MenuNest.Application/UseCases/Trips/UpdateStop/UpdateStopCommand.cs backend/src/MenuNest.Application/UseCases/Trips/UpdateStop/UpdateStopHandler.cs backend/src/MenuNest.WebApi/Controllers/TripsController.cs backend/tests/MenuNest.Application.UnitTests/Trips/UpdateStopHandlerTests.cs
git commit -m "feat(trips): read/write Stop.IsVisited via StopDto + UpdateStop (#24)"
```

---

### Task 4: Frontend API — `StopDto` type + non-invalidating `setStopVisited` mutation

**Files:**
- Modify: `frontend/src/shared/api/api.ts`
- Modify: `frontend/src/pages/trips/hooks/useSchedule.test.ts`

**Interfaces:**
- Consumes: the backend `StopDto.IsVisited` field (Task 3) and the `PATCH .../stops/{stopId}` endpoint accepting `{isVisited}` (Task 3).
- Produces: `StopDto.isVisited: boolean` (TS) and the `useSetStopVisitedMutation()` hook — arg `{tripId: string; stopId: string; isVisited: boolean}`, result `void`.

- [ ] **Step 1: Add `isVisited` to the `StopDto` TS interface** — in `api.ts`, replace line 503:

```ts
export interface StopDto { id: string; tripPlaceId: string; sequence: number; dwellMinutes: number; travelModeToReach: TravelMode; legToReach: LegDto | null; isVisited: boolean }
```

- [ ] **Step 2: Fix the existing test fixture so `tsc -b` still passes** — in `useSchedule.test.ts`, replace the `stop()` factory (lines 6–10) so every fixture satisfies the now-required field:

```ts
const stop = (id: string, seq: number, dwell: number, legSec: number | null) => ({
  id, tripPlaceId: `p${id}`, sequence: seq, dwellMinutes: dwell,
  travelModeToReach: 'Drive' as const, isVisited: false,
  legToReach: legSec == null ? null : {seconds: legSec, meters: 1000, encodedPolyline: null, source: 'Estimated' as const},
})
```

- [ ] **Step 3: Add the `setStopVisited` mutation** — in `api.ts`, inside the `api` endpoints builder (the block that defines `updateStop` at line 1313), add a sibling endpoint immediately after `updateStop`:

```ts
        setStopVisited: build.mutation<void, {tripId: string; stopId: string; isVisited: boolean}>({
            query: ({tripId, stopId, isVisited}) => ({
                url: `/api/trips/${tripId}/stops/${stopId}`, method: 'PATCH', body: {isVisited},
            }),
            // Display-only toggle (ADR-039/042): NO invalidatesTags, so getItinerary never
            // refetches (a refetch re-bills the Google Routes API + re-fetches Weather).
            // Optimistically patch every live getItinerary cache entry for this trip
            // (keyed by {tripId,tz,lat,lng} → possibly several), undo on failure.
            onQueryStarted: async ({tripId, stopId, isVisited}, {dispatch, queryFulfilled, getState}) => {
                const entries = api.util.selectInvalidatedBy(getState(), [{type: 'TripItinerary', id: tripId}])
                const patches = entries
                    .filter((e) => e.endpointName === 'getItinerary')
                    .map((e) =>
                        dispatch(
                            api.util.updateQueryData(
                                'getItinerary',
                                e.originalArgs as {tripId: string; tz?: string; lat?: number; lng?: number},
                                (draft) => {
                                    for (const day of draft) {
                                        const s = day.stops.find((x) => x.id === stopId)
                                        if (s) s.isVisited = isVisited
                                    }
                                },
                            ),
                        ),
                    )
                try {
                    await queryFulfilled
                } catch {
                    patches.forEach((p) => p.undo()) // revert; ItineraryTab surfaces the error (Task 5)
                }
            },
        }),
```

- [ ] **Step 4: Export the generated hook** — in `api.ts`, add to the hook export list next to `useUpdateStopMutation` (line 1486):

```ts
    useUpdateStopMutation,
    useSetStopVisitedMutation,
```

- [ ] **Step 5: Typecheck + build + run the pure-logic tests**

Run:
```bash
cd frontend
npm run build
npx vitest run src/pages/trips/hooks/useSchedule.test.ts
```
Expected: `tsc -b` + Vite build succeed (the fixture fix clears the TS2741 that the new required field would otherwise cause); the `useSchedule` tests pass unchanged (schedule cascade is untouched — ADR-039).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/shared/api/api.ts frontend/src/pages/trips/hooks/useSchedule.test.ts
git commit -m "feat(trips): StopDto.isVisited + non-invalidating setStopVisited mutation (#24)"
```

---

### Task 5: Frontend UI — checkbox, visited row, day rollup

**Files:**
- Modify: `frontend/src/pages/trips/components/FlagIcons.tsx`
- Modify: `frontend/src/pages/trips/components/ItineraryStopCard.tsx`
- Modify: `frontend/src/pages/trips/components/ItineraryTab.tsx`
- Modify: `frontend/src/pages/trips/trips-tokens.css`

**Interfaces:**
- Consumes: `StopDto.isVisited` and `useSetStopVisitedMutation` (Task 4).
- Produces: `ItineraryStopCard` props `isVisited: boolean` and `onToggleVisited: (next: boolean) => void`.

- [ ] **Step 1: Add the `CheckIcon`** — append to `FlagIcons.tsx` (matches the existing inline-SVG, never-emoji convention):

```tsx
export function CheckIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={3.2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <path d="M20 6 9 17l-5-5" />
    </svg>
  )
}
```

- [ ] **Step 2: Add CSS tokens** — in `trips-tokens.css`, inside the `.trips-page, .trip-detail { … }` block, after `--arr-rain-bg:#dbe9fb;` (line 28):

```css
  --visited:    #15803d;
  --visited-bg: #e7f6ec;
```

- [ ] **Step 3: Add the visited component rules** — in `trips-tokens.css`, after the `.stop-card.bad { … }` line (line 137):

```css
/* ── Visited ("มาแล้ว") — leading checkbox + de-emphasised row (ADR-040) ── */
.stop-check {
  flex: none;
  width: 40px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-right: 1px solid var(--border);
  background: #fafdfd;
}
.stop-check input { width: 19px; height: 19px; accent-color: var(--visited); cursor: pointer; }
.stop-card.visited { background: #f6f8fa; }
.stop-card.visited .stop-check { background: var(--visited-bg); border-right-color: #cfe9d7; }
/* Struck slate name (AA-safe ≈7.4:1) instead of whole-card opacity, which would push text below AA. */
.stop-card.visited .stop-name { text-decoration: line-through; text-decoration-thickness: 1.5px; color: #475569; }
.chip.visited { background: var(--visited-bg); color: var(--visited); display: inline-flex; align-items: center; gap: 4px; }
.chip.visited svg { width: 11px; height: 11px; }
/* Day-level progress pill on the dark day-summary bar */
.day-visited {
  display: inline-flex; align-items: center; gap: 6px;
  background: rgba(255, 255, 255, 0.06); border: 1px solid rgba(255, 255, 255, 0.14);
  border-radius: 999px; padding: 3px 10px; font-size: 11px; font-weight: 700; color: #dcfce7;
}
.day-visited .dot { width: 7px; height: 7px; border-radius: 50%; background: #4ade80; }
```

- [ ] **Step 4: Update `ItineraryStopCard.tsx`** — this file gains two props, a leading checkbox, the `visited` root class, and the green chip. Apply these edits:

Import (line 7) — add `CheckIcon`:
```tsx
import {ClockIcon, LockIcon, MoonIcon, CheckIcon} from './FlagIcons'
```

Props — add to the destructure (after `weatherLoading = false,`) and to the type (after `weatherLoading?: boolean`):
```tsx
  isVisited,
  onToggleVisited,
```
```tsx
  isVisited: boolean
  onToggleVisited: (next: boolean) => void
```

Root element + checkbox + chip — replace the return's opening (the `<div className="stop-card…">` through the `.stop-chips` opening) with:
```tsx
  return (
    <div className={`stop-card${flag ? ' ' + CARD_CLASS[flag.severity] : ''}${isVisited ? ' visited' : ''}`}>
      <label className="stop-check">
        <input
          type="checkbox"
          checked={isVisited}
          onChange={(e) => onToggleVisited(e.target.checked)}
          aria-label={`มาแล้ว: ${place.name}`}
        />
      </label>
      <div className="stop-rail">
        <div className="stop-arr">{arrival}</div>
        <div className="stop-dep">→{depart}</div>
      </div>
      <button className="stop-body" onClick={onEdit}>
        <div className="stop-name">{catEmoji(place.category)} {place.name}</div>
        <div className="stop-chips">
          {isVisited && <span className="chip visited"><CheckIcon />มาแล้ว</span>}
```

(The rest of `.stop-chips` — dwell + weather chips — and the remaining card markup are unchanged.)

- [ ] **Step 5: Wire `ItineraryTab.tsx`** — the mutation, the day rollup, and the two new props.

Import the hook (add to the `../../../shared/api/api` import block, lines 4–10):
```tsx
  useAddStopMutation,
  useSetStopVisitedMutation,
```

Instantiate the mutation (after `const [reorder] = useReorderStopsMutation()`, line 113):
```tsx
  const [setStopVisited] = useSetStopVisitedMutation()
```

Compute the rollup count (after `const existingTripPlaceIds = …`, line 170):
```tsx
  const visitedCount = scheduled.filter((s) => s.stop.isVisited).length
```

Render the pill inside `.day-stats`, after the "เดินทางรวม" span (before the closing `</div>` of `.day-stats`, line 232):
```tsx
          {scheduled.length > 0 && (
            <span className="day-visited">
              <span className="dot" />
              {visitedCount}/{scheduled.length} มาแล้ว
            </span>
          )}
```

Pass the two new props to `<ItineraryStopCard>` (add after `dwell={s.stop.dwellMinutes}`, line 285):
```tsx
                  isVisited={s.stop.isVisited}
                  onToggleVisited={async (next) => {
                    try {
                      await setStopVisited({tripId, stopId: s.stop.id, isVisited: next}).unwrap()
                    } catch (err) {
                      setActionError(getErrorMessage(err))
                    }
                  }}
```

- [ ] **Step 6: Typecheck + build**

Run: `cd frontend && npm run build`
Expected: `tsc -b` + Vite build succeed (card props are now both provided by `ItineraryTab` and consumed by `ItineraryStopCard`).

- [ ] **Step 7: Manual verification in the running app**

Run the SPA against the API (see the project `run` skill). Open a Trip with ≥2 stops and, with the browser Network tab open:
- Tick a stop → row de-emphasises (struck slate name, `#f6f8fa` background), green "✓ มาแล้ว" chip appears, checkbox stays checked. Confirm **only** the `PATCH /api/trips/{id}/stops/{stopId}` request fires — **no** `GET .../itinerary` and no Routes/Weather calls.
- The day bar pill increments (e.g. `1/2 มาแล้ว`).
- Reload the page → the tick persists.
- Untick → reverts. A visited stop that has a timing flag or weather chips still shows them (de-emphasised, never hidden).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/pages/trips/components/FlagIcons.tsx frontend/src/pages/trips/components/ItineraryStopCard.tsx frontend/src/pages/trips/components/ItineraryTab.tsx frontend/src/pages/trips/trips-tokens.css
git commit -m "feat(trips): 'มาแล้ว' visited checkbox, row treatment + day rollup on itinerary (#24)"
```

---

### Task 6: Apply the migration to prod + close the issue

**Files:** none (deployment step — spec §8, project rule in CLAUDE.md).

- [ ] **Step 1: Confirm the terminal `az` session is the SQL Entra admin**

Run: `az account show`
Expected: `Pay-As-You-Go` / `thodsaphonSP@hotmail.co.th` (the SQL admin). If not, `az login` as that account first.

- [ ] **Step 2: Preview the exact SQL that will run against prod**

Run:
```bash
cd backend
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```
Expected: only the guarded `AddStopIsVisited` block (adds the `IsVisited` bit column) — nothing destructive.

- [ ] **Step 3: Apply the migration to the prod DB by hand**

Run:
```bash
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```
Expected: `Applying migration '<timestamp>_AddStopIsVisited'` then `Done.`

- [ ] **Step 4: Deploy + smoke-test**

Push/deploy via the existing CD (`main_menunest.yml`). Then against prod: load a Trip's itinerary (must not 500 — proves the column exists), tick a stop, reload, confirm it persisted.

- [ ] **Step 5: Final commit closing the issue** (if any doc/state changes remain to record — e.g. marking the plan done):

```bash
git add docs/superpowers/plans/2026-07-12-trip-stop-visited.md
git commit -m "docs(trips): mark Stop Visited plan complete (closes #24)"
```

(If no file remains to change, close #24 via the final feature commit's trailer instead — do not leave the issue open.)

---

## Self-Review

**1. Spec coverage.**
- §2 domain field + mutator → Task 1. §3 column + migration + manual apply → Tasks 2 & 6. §4 command/handler/controller/DTO/construction sites + the positional-default gotcha → Task 3. §5.1 TS type + non-invalidating optimistic mutation → Task 4. §5.2 card checkbox/class/chip + §5.4 CSS/tokens/AA treatment → Task 5. §5.3 wiring + error handling + day rollup (guarded for empty day) → Task 5. §5.5 toggle both ways → Tasks 3/5. §6 UI matches mock (AA refinement) → Task 5. §7 tests: domain (Task 1), handler true/false/null (Task 3), fixture fix (Task 4), manual behaviour (Task 5). §8 rollout → Task 6. §9 deferred → not built (correct). All covered.

**2. Placeholder scan.** No `TBD`/"handle edge cases"/"similar to Task N". Every code step shows the actual code; the optimistic-patch mechanism is written out in full (Task 4 Step 3), not deferred.

**3. Type consistency.** `IsVisited`/`SetVisited` (C#) and `isVisited`/`onToggleVisited`/`setStopVisited`/`useSetStopVisitedMutation` (TS) are used identically across Tasks 1→3 (backend) and 4→5 (frontend). `StopDto` gains the trailing field in both the C# record (Task 3) and the TS interface (Task 4) in the same position. `UpdateStopCommand`'s 5th param `IsVisited` is defaulted (`= null`) so the untouched `TripTools.cs:140` call site (4 args) still compiles — verified against the real file.
