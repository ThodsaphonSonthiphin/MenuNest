let audioCtx: AudioContext | null = null

const ensureAudioCtx = (): AudioContext | null => {
  if (typeof window === 'undefined') return null
  if (audioCtx) return audioCtx
  type WindowWithWebkit = Window & {
    webkitAudioContext?: typeof AudioContext
  }
  const Ctor =
    window.AudioContext ?? (window as WindowWithWebkit).webkitAudioContext
  if (!Ctor) return null
  audioCtx = new Ctor()
  return audioCtx
}

export const playCycleEndSound = (): void => {
  const ctx = ensureAudioCtx()
  if (!ctx) return
  // Two short sine pulses — ~120ms each, separated by a 80ms gap. Loud
  // enough to be obvious without an external asset.
  const now = ctx.currentTime
  const beep = (start: number) => {
    const osc = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.type = 'sine'
    osc.frequency.value = 880
    gain.gain.setValueAtTime(0.0001, start)
    gain.gain.exponentialRampToValueAtTime(0.25, start + 0.02)
    gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.12)
    osc.connect(gain).connect(ctx.destination)
    osc.start(start)
    osc.stop(start + 0.14)
  }
  beep(now)
  beep(now + 0.2)
}

export const requestNotificationPermissionOnce = async (): Promise<void> => {
  if (typeof Notification === 'undefined') return
  if (Notification.permission !== 'default') return
  try {
    await Notification.requestPermission()
  } catch {
    /* some browsers throw on unsupported contexts — ignore */
  }
}

export const fireForegroundNotification = (title: string, body: string): void => {
  if (typeof Notification === 'undefined') return
  if (Notification.permission !== 'granted') return
  try {
    new Notification(title, { body, tag: 'menunest-pomodoro' })
  } catch {
    /* no-op */
  }
}

export interface SchedulePayload {
  id: string
  fireAt: number
  title: string
  body: string
}

const postToSW = (message: unknown): void => {
  if (typeof navigator === 'undefined') return
  const controller = navigator.serviceWorker?.controller
  if (!controller) return
  try {
    controller.postMessage(message)
  } catch {
    /* no-op */
  }
}

export const scheduleBackgroundNotification = (payload: SchedulePayload): void => {
  postToSW({ type: 'POMODORO_SCHEDULE', payload })
}

export const cancelBackgroundNotification = (id: string): void => {
  postToSW({ type: 'POMODORO_CANCEL', id })
}
