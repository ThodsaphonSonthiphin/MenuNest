import { expect } from '@playwright/test'
import { test } from './fixtures/healthFixture'
import {
  installPomodoroSpies,
  readNotifs,
  readPermAsks,
  readPlays,
  readSwMsgs,
} from './helpers/pomodoroSpies'

test.describe('Pomodoro — notifications + sound + SW', () => {
  test.beforeEach(async ({ authedPage: page, context }) => {
    await context.grantPermissions(['notifications'])
    await installPomodoroSpies(page)
    await page.clock.install({ time: new Date('2026-06-01T09:00:00Z') })
    // Sentinel — clear LS once per test, preserve across reload.
    await page.addInitScript(() => {
      if (!sessionStorage.getItem('__pomo_lsCleared')) {
        localStorage.clear()
        sessionStorage.setItem('__pomo_lsCleared', '1')
      }
    })
  })

  test('first Start asks for notification permission exactly once', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.getByTestId('pomo-pause').click()
    await page.getByTestId('pomo-start').click()

    const asks = await readPermAsks(page)
    expect(asks).toBe(1)
  })

  test('cycle completion plays sound + fires a Notification', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(25 * 60_000)

    const notifs = await readNotifs(page)
    expect(notifs.length).toBe(1)
    expect(notifs[0].title).toBe('Focus done')

    const plays = await readPlays(page)
    // playCycleEndSound emits two beeps → two oscillator.start calls.
    expect(plays).toBeGreaterThanOrEqual(2)
  })

  test('sound toggle off suppresses audio but still fires notification', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-settings-toggle').click()
    await page.getByTestId('pomo-sound-toggle').click() // off
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(25 * 60_000)

    expect(await readPlays(page)).toBe(0)
    expect((await readNotifs(page)).length).toBe(1)
  })

  test('notification toggle off suppresses both Notification and SW schedule', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-settings-toggle').click()
    await page.getByTestId('pomo-notif-toggle').click() // off
    await page.getByTestId('pomo-start').click()
    await page.clock.fastForward(25 * 60_000)

    expect(await readNotifs(page)).toEqual([])
    const sw = await readSwMsgs(page)
    expect(sw.filter((m) => m.type === 'POMODORO_SCHEDULE')).toEqual([])
  })

  test('Start sends POMODORO_SCHEDULE; Pause + Reset send POMODORO_CANCEL', async ({
    authedPage: page,
  }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    await page.getByTestId('pomo-pause').click()
    await page.getByTestId('pomo-reset').click()

    const msgs = await readSwMsgs(page)
    const types = msgs.map((m) => m.type)
    expect(types).toContain('POMODORO_SCHEDULE')
    expect(types.filter((t) => t === 'POMODORO_CANCEL').length).toBeGreaterThanOrEqual(2)
    const sched = msgs.find((m) => m.type === 'POMODORO_SCHEDULE')!
    expect(sched.payload!.title).toBe('Focus done')
    expect(typeof sched.payload!.fireAt).toBe('number')
  })

  test('visibility transitions toggle SW scheduling', async ({ authedPage: page }) => {
    await page.goto('/pomodoro')
    await page.getByTestId('pomo-start').click()
    // Clear the initial SCHEDULE so we can observe the visibility behaviour.
    await page.evaluate(
      () => ((window as unknown as { __pomoSwMsgs: unknown[] }).__pomoSwMsgs = []),
    )

    await page.evaluate(() => {
      Object.defineProperty(document, 'hidden', { configurable: true, value: true })
      document.dispatchEvent(new Event('visibilitychange'))
    })
    await page.evaluate(() => {
      Object.defineProperty(document, 'hidden', { configurable: true, value: false })
      document.dispatchEvent(new Event('visibilitychange'))
    })

    const msgs = await readSwMsgs(page)
    const types = msgs.map((m) => m.type)
    expect(types).toEqual(['POMODORO_SCHEDULE', 'POMODORO_CANCEL'])
  })
})
