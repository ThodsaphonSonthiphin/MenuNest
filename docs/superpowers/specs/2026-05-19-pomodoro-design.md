# Pomodoro Timer — Design

**Status:** Approved — ready for implementation planning
**Date:** 2026-05-19
**Author:** Brainstormed with Claude (MenuNest project)

## Problem

MenuNest currently covers meal planning, stock, shopping, budget, AI
assistance, and the personal-scope Health (migraine) module. The user
wants a dedicated focus timer surfaced as a top-level module so they
can run Pomodoro cycles without leaving the app. The feature must be
discoverable from the main navigation, work for a single user (no
family sharing), and survive page navigation / tab close.

## Goals

- Add `/pomodoro` as a top-level route reachable from the main NavBar.
- Personal-scope (auth required, family NOT required) — same access
  shape as `/health`.
- Basic Pomodoro semantics: alternating Focus and Break cycles with
  Start / Pause / Reset controls and user-adjustable durations.
- State persists in `localStorage` only — no backend, no cross-device
  sync.
- Reload / tab close / navigate-and-return restores the correct
  remaining time, because state is derived from a `startedAt`
  timestamp rather than an accumulating counter.
- Cycle-end notification fires:
  - While the page is visible: via the foreground tick (sound +
    `Notification` + mode auto-switch).
  - While the tab is backgrounded: via a Service Worker that holds a
    scheduled `showNotification` call, with the caveats documented
    under *Known limitations* below.

## Non-goals (MVP)

- Task list / "what are you focusing on" association.
- Session history, daily totals beyond a simple in-day counter, or
  any analytics view.
- Long-break-every-N-cycles automation.
- Backend persistence or sync across devices/browsers.
- NavBar indicator showing the timer running while the user is on
  another page. (Picking the simpler architecture — see *Approach*
  below — explicitly defers this.)
- iOS web push registration / VAPID. Background notification on iOS
  is best-effort and depends on the user installing the app as a PWA.

## Approach

State lives entirely in `localStorage` under a single versioned key.
The `/pomodoro` page mounts, reads the key, and runs a 1-second
`setInterval` that re-derives `remaining` from `startedAt` on every
tick. There is **no React context** lifted above the route — when the
user navigates away from `/pomodoro` the in-page interval stops, but
the timestamp in `localStorage` is unaffected, so returning to the
page (or reloading, or reopening the tab) restores the correct state.

Background notification is handled by a small dedicated Service
Worker (`pomodoro-sw.js`) that the page registers on first visit.
When the timer starts or resumes, the page sends the worker a
`SCHEDULE` message with `fireAt`, `title`, and `body`. The worker
sets a `setTimeout` and calls `self.registration.showNotification`
when it fires. Pause / Reset / page-visible transitions send
`CANCEL` to keep the worker and the page from racing.

This is "Approach A" from brainstorming, chosen over a global
`TimerProvider` because the MVP does not need cross-route ticking or
a NavBar badge, and the simpler shape is closer to the project's
`drop-complex-extras` preference.

## Data Model (localStorage)

Single key: `menunest:pomodoro:v1`. Versioned so schema changes can
ship without colliding with older values.

```ts
type PomodoroState = {
  status: 'idle' | 'running' | 'paused'
  mode: 'focus' | 'break'
  startedAt: number | null           // epoch ms when current mode started
  pausedRemainingMs: number | null   // remaining at the moment of pause
  settings: {
    focusMin: number                 // default 25, valid range 1–90
    breakMin: number                 // default 5,  valid range 1–30
    soundOn: boolean                 // default true
    notifOn: boolean                 // default true
  }
  scheduledNotifId: string | null    // id mirrored to the Service Worker
  dailyCount: { date: string; focusCompleted: number }  // 'YYYY-MM-DD' local
}
```

Derived (not stored):

- `remaining = (status === 'running')
    ? max(0, durationFor(mode) - (now - startedAt))
    : (status === 'paused' ? pausedRemainingMs : durationFor(mode))`
- `durationFor(mode) = settings[mode + 'Min'] * 60_000`

`dailyCount` resets to `{ date: today, focusCompleted: 0 }` whenever
the page reads state and the stored `date` is not today.

## File Structure

Follows the project's React structure convention (separate hooks from
UI — see `react-structure` skill / `pages/health` pattern):

```
frontend/src/pages/pomodoro/
  index.ts                 // export { PomodoroPage }
  PomodoroPage.tsx         // presentational
  usePomodoroTimer.ts      // hook: state, tick, transitions, persistence
  pomodoroStorage.ts       // pure load/save/migrate, easy to unit test
  notifications.ts         // permission, foreground Notification, SW messaging
  PomodoroPage.css
frontend/public/
  pomodoro-sw.js           // service worker dedicated to this feature
  pomodoro-ding.mp3        // cycle-end sound (royalty-free; sourced separately)
```

Router and NavBar edits:

- `frontend/src/router.tsx` — add `{ path: '/pomodoro', element:
  <PomodoroPage /> }` under the personal-scope `AppLayout` children
  (alongside `/health/*`, NOT inside `FamilyRequiredRoute`).
- `frontend/src/shared/components/NavBar.tsx` — add
  `{ to: '/pomodoro', label: '⏱️ Pomodoro' }` to `navItems` so it
  appears in both desktop links and the mobile drawer.

## Timer Logic

`usePomodoroTimer` exposes:

```ts
{
  status, mode, remainingMs, settings, dailyFocusCount,
  start(): void,    // status = running, startedAt = now
  pause(): void,    // pausedRemainingMs = remaining, status = paused
  resume(): void,   // startedAt = now - (durationFor(mode) - pausedRemainingMs)
  reset(): void,    // status = idle, startedAt = null, pausedRemainingMs = null
  updateSettings(partial): void,
}
```

Internal tick:

1. `setInterval(tick, 1000)` while mounted.
2. Each tick re-reads `startedAt` (the source of truth) and renders
   the new `remaining`.
3. When `remaining` crosses zero and `status === 'running'`:
   - If foreground: play sound, fire `new Notification(...)`, switch
     mode (`focus → break` or `break → focus`), reset `startedAt` to
     `now`, increment `dailyCount.focusCompleted` if the cycle that
     just ended was `focus`.
   - The Service Worker will still fire its scheduled notification if
     the page beat it — `notifications.ts` cancels the SW timer on
     visibility-visible to keep this single-fire.

Settings changes while idle take effect immediately. Settings changes
while running or paused only affect the **next** cycle (current cycle
keeps its duration so the visible countdown doesn't jump).

## Notification Flow

```
[start/resume] ──► Notification.requestPermission (once)
       │
       ├──► foreground: nothing extra — tick will fire Notification
       │    locally when remaining ≤ 0
       │
       └──► SW.postMessage({ type: 'SCHEDULE',
                              id, fireAt, title, body })
              │
              └──► sw setTimeout → registration.showNotification

[pause / reset]
       └──► SW.postMessage({ type: 'CANCEL', id })

[visibility → visible]
       └──► SW.postMessage({ type: 'CANCEL', id })
            so only the foreground tick fires the notification

[visibility → hidden]
       └──► SW.postMessage({ type: 'SCHEDULE', ... })
            in case the page is unloaded before the cycle ends
```

The SW keeps a `Map<id, timeoutHandle>` in-memory. If the worker
itself is evicted (no client + idle), the scheduled callback is
lost — see *Known limitations*.

## UI

Single page, no sub-routes. Layout:

- Header strip: badge "Focus" / "Break", small text
  "Pomodoros today: N".
- Center: `MM:SS` set in a very large font (≥ 80px on desktop,
  responsive down).
- Below the time: primary action button — Start (idle), Pause
  (running), or Resume (paused) — plus a secondary Reset button.
- Collapsible "Settings" panel under the controls:
  - Focus duration slider 1–90 min
  - Break duration slider 1–30 min
  - Sound toggle
  - Notification toggle (disables permission request when off)
- Mobile-first: large touch targets, the timer dominates the viewport
  so the screen reads like a focus mode even with the NavBar present.

Components use Syncfusion Buttons to match the rest of MenuNest (see
`NavBar.tsx` for the existing import pattern).

## Testing

Testing strategy: **Playwright covers every code-path that runs in
the browser.** Unit tests (Vitest) are kept narrow — only for the
pure modules where unit-level coverage is meaningfully cheaper than
E2E. The few remaining gaps are OS-level concerns that no browser
test framework can drive (true app-swap on mobile, iOS PWA push) and
are listed under *Manual test matrix*.

### Unit tests (Vitest)

- `pomodoroStorage.ts` — pure functions. Load with empty storage,
  load with current version, load with unrecognised version (falls
  back to defaults, does not throw), save round-trip, daily-count
  reset across date boundary. Schema migration path is unit-only
  because constructing legacy states from the UI would be artificial.

### E2E tests (Playwright)

Determinism: every spec installs Playwright's mock clock via
`await page.clock.install({ time: '2026-06-01T09:00:00Z' })` before
navigating. Time is advanced explicitly with
`page.clock.fastForward(ms)` or `page.clock.runFor(ms)` — no real
waits. Storage is reset between tests via `page.context().clearCookies()`
plus `localStorage.clear()` in a `beforeEach`. Tests use the project's
existing `authedPage` fixture (`frontend/e2e/fixtures/healthFixture.ts`)
because `/pomodoro` sits behind `ProtectedRoute`; family is not
required so no family mock is needed.

Notification permission is pre-granted via
`browserContext.grantPermissions(['notifications'])`. Browser
`Notification` calls are captured by installing a spy in
`page.addInitScript` that pushes invocations onto `window.__notifs`,
which assertions read back.

Service-Worker messages are captured similarly: an init script
replaces `navigator.serviceWorker.controller.postMessage` with a
recording proxy onto `window.__swMessages` so tests can assert
SCHEDULE / CANCEL traffic without driving a real worker.

Specs (one file per concern, mirroring the `health.*.spec.ts` style):

1. `pomodoro.access.spec.ts`
   - anonymous visit to `/pomodoro` is bounced to `/login`
   - authed user with NO family can reach `/pomodoro` (proves
     personal-scope)
   - NavBar shows "⏱️ Pomodoro" link on desktop viewport
   - Mobile drawer (375px viewport) lists the link; clicking it
     navigates to `/pomodoro` and closes the drawer

2. `pomodoro.state-machine.spec.ts`
   - idle → Start → status=running, badge="Focus", time counts down
     when clock advances 1s
   - running → Pause → status=paused, displayed time freezes after
     `fastForward(5s)`
   - paused → Resume → status=running, remaining picks up from the
     paused value (not the full duration)
   - running → Reset → status=idle, time restored to full focus
     duration
   - paused → Reset → same as above

3. `pomodoro.cycle-transitions.spec.ts`
   - Start focus, `fastForward(focusMin * 60_000)` → mode flips to
     "Break", `dailyCount.focusCompleted` increments
   - Continue: `fastForward(breakMin * 60_000)` → mode flips back to
     "Focus", daily count unchanged on break completion
   - Run three full focus cycles → daily count reads "3"

4. `pomodoro.persistence.spec.ts`
   - Start, `fastForward(3 min)`, `page.reload()` → timer shows the
     correct remaining (focusMin*60 − 180s), still running
   - Start, `fastForward(3 min)`, pause, reload → still paused with
     same remaining
   - Reset, reload → state is idle, time at full duration
   - Daily count: advance clock past midnight via
     `page.clock.setSystemTime(...)`, reload → count resets to 0,
     stored `date` matches new day

5. `pomodoro.cross-navigation.spec.ts`
   - Start, navigate to `/recipes` (mock family so the route resolves),
     wait, navigate back to `/pomodoro` → state restored, remaining
     matches elapsed clock time
   - Start, click NavBar brand to go to `/`, return → same assertion

6. `pomodoro.settings.spec.ts`
   - While idle: change focus slider from 25 → 5, displayed time
     updates immediately to 05:00
   - While running: change focus slider — current MM:SS does NOT
     jump; press Reset, displayed time now reflects the new duration
   - Sound toggle off: cycle completion does NOT trigger
     `HTMLAudioElement.play` (spied)
   - Notification toggle off: starting the timer does NOT call
     `Notification.requestPermission`
   - Settings round-trip via `page.reload()` → values preserved

7. `pomodoro.notifications.spec.ts`
   - First Start triggers `Notification.requestPermission` exactly
     once; subsequent Start within the session does not re-prompt
   - Foreground cycle completion: `window.__notifs` records one
     `new Notification(...)` per transition with the expected title
   - When the page is hidden (`page.evaluate` to dispatch a
     `visibilitychange` event with `document.hidden = true`), Start
     sends a `SCHEDULE` SW message; Pause sends `CANCEL`; returning
     to visible sends another `CANCEL` so the foreground tick owns
     the fire
   - Audio element loads `pomodoro-ding.mp3` and `.play()` is
     invoked at cycle end (spy via init script)

8. `pomodoro.smoke.spec.ts` (kept short, mirrors
   `health.smoke.spec.ts`)
   - `/pomodoro` reachable, renders, no console errors
   - PWA service worker (`/sw.js`) still registers — Pomodoro
     shouldn't accidentally break the existing migraine SW

Coverage status per case from brainstorming:

| Case | Covered by | Spec |
|---|---|---|
| State transitions | Playwright | state-machine |
| Cycle auto-switch + daily count | Playwright | cycle-transitions |
| Reload / restore from startedAt | Playwright | persistence |
| Cross-page navigation | Playwright | cross-navigation |
| Settings while idle / running | Playwright | settings |
| Sound + Notification API | Playwright (spied) | notifications |
| SW SCHEDULE / CANCEL messages | Playwright (spied) | notifications |
| Personal scope (no family needed) | Playwright | access |
| NavBar link desktop + drawer | Playwright | access |
| localStorage versioning + bad data | Vitest | unit |
| Daily-count midnight rollover | Playwright (mock clock) | persistence |

### Manual test matrix (not covered by Playwright)

These cases involve OS-level behaviour outside the browser test
harness's reach. They are checked once before release and after any
change to `pomodoro-sw.js`:

- Android Chrome foreground / backgrounded tab — BG notif expected
  to fire.
- iOS Safari foreground — foreground notif expected to fire.
- iOS Safari with app swapped to another iOS app — BG notif may not
  fire; state must still be correct on return.
- iOS PWA (Add to Home Screen) swapped to another app — BG notif
  expected to fire.
- Desktop Chrome with the tab evicted (long idle) — verify that on
  return, state derives correctly from `startedAt` even after the
  SW was unloaded.

## Known limitations

- **iOS Safari background notification.** On iOS, Service Workers
  are paused when there are no visible clients. A scheduled
  `setTimeout` inside the SW will not fire reliably while the user
  is in another app unless the user has installed MenuNest as a PWA
  (Add to Home Screen). When the limitation hits, the worst case is
  a missed *notification* — the next time the user returns to
  `/pomodoro`, the page derives the correct state from `startedAt`
  and shows the completed cycle.
- **No cross-device sync.** Settings and the running timer are
  per-browser. This is intentional for MVP.
- **No active-timer indicator outside `/pomodoro`.** If the user
  navigates to another module, the in-page interval stops; returning
  to `/pomodoro` resyncs from `localStorage`. A NavBar indicator
  would require lifting state into a context provider, which is
  explicitly deferred.

## Out-of-scope follow-ups (Phase 2 candidates)

These are recorded so they don't drift back into MVP scope:

- Task association ("what am I focusing on right now?").
- Session history persisted to backend with per-day focus minutes
  chart.
- Long-break-every-N-cycles automation.
- NavBar mini-indicator with remaining time.
- Web Push registration for reliable iOS background notification.
