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

- `pomodoroStorage.ts` — Vitest unit tests covering load with empty
  storage, load with current version, load with unrecognised version
  (falls back to defaults, does not throw), save round-trip, daily
  count reset across date boundary.
- `usePomodoroTimer.ts` — React Testing Library with
  `vi.useFakeTimers()`. Cases: start → tick → reach zero auto-switch;
  pause preserves remaining; resume continues from paused remaining;
  reset clears state; settings change while running doesn't shrink
  current cycle.
- `notifications.ts` — unit tests for permission gating and message
  shape; mock `navigator.serviceWorker`.
- E2E (Playwright) — one smoke spec: visit `/pomodoro`, configure
  short durations via the settings panel, click Start, wait for mode
  transition, assert badge flipped to "Break". Notification and
  Service Worker behaviour are NOT covered by E2E (treated as manual
  test territory).
- Manual test matrix to document in the implementation plan: desktop
  Chrome foreground, desktop Chrome backgrounded tab, Android Chrome
  foreground, Android Chrome with app swapped away, iOS Safari
  foreground, iOS Safari swapped (expected: BG notif may not fire),
  iOS PWA installed and swapped (expected: BG notif fires).

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
