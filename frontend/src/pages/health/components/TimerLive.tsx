import { useEffect, useState } from 'react'

/**
 * Live elapsed-time readout for the Active Episode page. Re-renders every
 * 30 seconds — fast enough to feel alive without taxing the device. The
 * episode page itself also polls the backend every 30s, so the perceived
 * "ongoing" feeling stays in sync with server-side state.
 */
export interface TimerLiveProps {
  startedAt: string
  /** Wrap the value text in a span; useful when embedded in a sentence. */
  className?: string
}

export function TimerLive({ startedAt, className }: TimerLiveProps) {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), 30_000)
    return () => window.clearInterval(t)
  }, [])

  const startMs = new Date(startedAt).getTime()
  const diffMin = Math.max(0, Math.floor((now - startMs) / 60_000))
  const hours = Math.floor(diffMin / 60)
  const minutes = diffMin % 60
  const text = hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`

  return <span className={className}>{text}</span>
}
