import { useEffect, useMemo, useState } from 'react'
import { useWebPushSubscription } from '../hooks/useWebPushSubscription'

/**
 * Friendly nudge asking the user to enable web push *after* they've
 * proven the migraine feature is useful (i.e. they have at least one
 * episode in history). Per the plan:
 *
 *   "Prompt permission at right time: NOT on first launch. Trigger on
 *   first LogIntake action with a friendly modal."
 *
 * Phase 1 implementation simplifies that to "first time the user lands
 * on Home with non-zero history" — easier to reason about than wiring
 * a side-effect into useLogIntake, and still hits the same UX intent
 * (we never ask cold). After a "ทีหลัง" dismissal we wait 7 days before
 * re-prompting; once granted/denied we never prompt again because the
 * browser itself will not show another prompt anyway.
 *
 * The actual subscribe flow lives in `useWebPushSubscription`; this
 * component is purely the gate + UI.
 */
export interface PushPermissionPromptProps {
  /** Set true once the parent has loaded data confirming the user has
   *  at least one historical episode. We don't prompt on a blank Home. */
  shouldOffer: boolean
}

const STORAGE_KEY = 'health-push-prompt-dismissed-at'
const DISMISS_WINDOW_MS = 7 * 24 * 60 * 60 * 1000

function readDismissedAt(): number | null {
  if (typeof window === 'undefined') return null
  const raw = window.localStorage.getItem(STORAGE_KEY)
  if (!raw) return null
  const n = Number(raw)
  return Number.isFinite(n) ? n : null
}

export function PushPermissionPrompt({ shouldOffer }: PushPermissionPromptProps) {
  const push = useWebPushSubscription()
  const [open, setOpen] = useState(false)
  const [dismissedAt, setDismissedAt] = useState<number | null>(() =>
    readDismissedAt(),
  )

  // Only consider prompting when:
  //   1. the browser supports web push at all,
  //   2. the user hasn't already granted/denied,
  //   3. they're not already subscribed (defence in depth),
  //   4. parent says we have a reason to ask, and
  //   5. their last "ทีหลัง" was more than 7 days ago (or never).
  const eligible = useMemo(() => {
    if (!push.isSupported) return false
    if (push.permission !== 'default') return false
    if (push.isSubscribed) return false
    if (!shouldOffer) return false
    if (dismissedAt && Date.now() - dismissedAt < DISMISS_WINDOW_MS) return false
    return true
  }, [
    push.isSupported,
    push.permission,
    push.isSubscribed,
    shouldOffer,
    dismissedAt,
  ])

  useEffect(() => {
    if (eligible) setOpen(true)
  }, [eligible])

  if (!open) return null

  const dismiss = () => {
    const now = Date.now()
    setDismissedAt(now)
    try {
      window.localStorage.setItem(STORAGE_KEY, String(now))
    } catch {
      /* Storage can be disabled (Safari private mode) — fall back to
         the in-memory flag, which is good enough for this session. */
    }
    setOpen(false)
  }

  const accept = async () => {
    try {
      await push.subscribe()
    } finally {
      // Whether the subscription succeeded or the user denied, we
      // close the modal. The hook owns the error state and the
      // settings page surfaces it.
      setOpen(false)
    }
  }

  return (
    <div className="health-modal-backdrop" role="dialog" aria-modal="true">
      <div className="health-modal">
        <div className="health-modal__title">🔔 อยากให้เราเตือนหลังกินยามั้ย?</div>
        <div style={{ fontSize: 14, lineHeight: 1.5, color: 'var(--hl-text-muted)' }}>
          เราจะส่ง notification ถามว่ายาออกฤทธิ์รึยังประมาณ 30 นาทีหลังกินยา
          คุณตอบได้แค่แตะปุ่มในแจ้งเตือน (ไม่ต้องเปิดแอป)
        </div>
        <div className="health-modal__actions">
          <button
            type="button"
            className="health-action-btn"
            onClick={dismiss}
            disabled={push.isLoading}
          >
            ทีหลัง
          </button>
          <button
            type="button"
            className="health-action-btn health-action-btn--primary"
            onClick={accept}
            disabled={push.isLoading}
          >
            {push.isLoading ? 'กำลัง…' : 'อนุญาต'}
          </button>
        </div>
      </div>
    </div>
  )
}
