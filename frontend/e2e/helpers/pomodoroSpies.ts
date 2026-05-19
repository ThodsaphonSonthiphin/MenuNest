import type { Page } from '@playwright/test'

export interface NotifRecord {
  title: string
  body?: string
}

export interface SWMessageRecord {
  type: string
  payload?: { id?: string; fireAt?: number; title?: string; body?: string }
  id?: string
}

export const installPomodoroSpies = async (page: Page) => {
  await page.addInitScript(() => {
    type W = Window & {
      __pomoNotifs?: { title: string; body?: string }[]
      __pomoPlays?: number
      __pomoSwMsgs?: unknown[]
      __pomoPermAsks?: number
    }
    const w = window as unknown as W
    w.__pomoNotifs = []
    w.__pomoPlays = 0
    w.__pomoSwMsgs = []
    w.__pomoPermAsks = 0

    // --- Notification spy ----------------------------------------------
    type NotifCtor = new (title: string, options?: NotificationOptions) => Notification
    const Real = window.Notification as unknown as NotifCtor
    const Fake = function (title: string, options?: NotificationOptions) {
      w.__pomoNotifs!.push({ title, body: options?.body })
      try {
        return new Real(title, options)
      } catch {
        // In headless contexts the real Notification constructor may
        // throw; we only care about recording the call.
        return {} as Notification
      }
    } as unknown as NotifCtor & {
      permission: NotificationPermission
      requestPermission: () => Promise<NotificationPermission>
    }
    // Start as 'default' so the first requestPermission() call goes
    // through; flip to 'granted' on first ask so subsequent Start clicks
    // short-circuit (matching real browser behaviour after user accepts).
    Object.defineProperty(Fake, 'permission', {
      value: 'default',
      configurable: true,
      writable: true,
    })
    Fake.requestPermission = async () => {
      w.__pomoPermAsks!++
      Object.defineProperty(Fake, 'permission', {
        value: 'granted',
        configurable: true,
        writable: true,
      })
      return 'granted'
    }
    Object.assign(window, { Notification: Fake })

    // --- AudioContext spy: count oscillator starts as "plays" ----------
    const RealCtx =
      window.AudioContext ||
      (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext
    if (RealCtx) {
      const orig = RealCtx.prototype.createOscillator
      RealCtx.prototype.createOscillator = function () {
        const osc = orig.call(this)
        const origStart = osc.start.bind(osc)
        osc.start = function (when?: number) {
          w.__pomoPlays!++
          return origStart(when)
        }
        return osc
      }
    }

    // --- serviceWorker.controller.postMessage spy ----------------------
    // The page may not have an active controller during tests, so we
    // install a stub controller object the very first time the code
    // tries to read it. Some Chromium versions seal `controller` on the
    // prototype — in that case we override `navigator.serviceWorker`
    // itself with a stub object.
    const stubController = {
      postMessage(msg: unknown) {
        w.__pomoSwMsgs!.push(msg)
      },
    }
    let controllerInstalled = false
    try {
      Object.defineProperty(navigator.serviceWorker, 'controller', {
        configurable: true,
        get() {
          return stubController
        },
      })
      controllerInstalled = true
    } catch {
      /* fallthrough — try the navigator.serviceWorker override below */
    }
    if (!controllerInstalled) {
      try {
        Object.defineProperty(navigator, 'serviceWorker', {
          configurable: true,
          get() {
            return { controller: stubController }
          },
        })
      } catch {
        /* some browsers seal it — last-ditch ignore */
      }
    }
  })
}

export const readNotifs = (page: Page) =>
  page.evaluate(
    () => (window as unknown as { __pomoNotifs: NotifRecord[] }).__pomoNotifs,
  )

export const readPlays = (page: Page) =>
  page.evaluate(() => (window as unknown as { __pomoPlays: number }).__pomoPlays)

export const readSwMsgs = (page: Page) =>
  page.evaluate(
    () => (window as unknown as { __pomoSwMsgs: SWMessageRecord[] }).__pomoSwMsgs,
  )

export const readPermAsks = (page: Page) =>
  page.evaluate(() => (window as unknown as { __pomoPermAsks: number }).__pomoPermAsks)
