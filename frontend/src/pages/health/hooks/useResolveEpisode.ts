import { useCallback, useEffect, useState } from 'react'
import { useResolveEpisodeMutation } from '../../../shared/api/api'

/**
 * Wraps the resolve mutation with a tiny inline toast so the user sees a
 * confirmation when they tap "หายแล้ว". The toast auto-dismisses after
 * 2s; we return a `toast` value so the page can render it.
 */
export function useResolveEpisode() {
  const [trigger, state] = useResolveEpisodeMutation()
  const [toast, setToast] = useState<string | null>(null)

  useEffect(() => {
    if (!toast) return
    const t = window.setTimeout(() => setToast(null), 2000)
    return () => window.clearTimeout(t)
  }, [toast])

  const resolveEpisode = useCallback(
    async (id: string, severityAfter = 0) => {
      const result = await trigger({ id, severityAfter }).unwrap()
      setToast('✅ บันทึกแล้ว — episode resolved')
      return result
    },
    [trigger],
  )

  return {
    resolveEpisode,
    isLoading: state.isLoading,
    error: state.error,
    toast,
  }
}
