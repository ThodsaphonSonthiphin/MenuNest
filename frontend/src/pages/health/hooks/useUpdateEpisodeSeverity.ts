import { useCallback, useEffect, useState } from 'react'
import { useUpdateEpisodeMutation } from '../../../shared/api/api'

/**
 * Updates just the severity on an existing episode and shows a brief
 * toast. The full update endpoint accepts many other fields — we keep
 * the surface area focused so the "update severity" modal stays a
 * single-purpose interaction.
 */
export function useUpdateEpisodeSeverity() {
  const [trigger, state] = useUpdateEpisodeMutation()
  const [toast, setToast] = useState<string | null>(null)

  useEffect(() => {
    if (!toast) return
    const t = window.setTimeout(() => setToast(null), 2000)
    return () => window.clearTimeout(t)
  }, [toast])

  const updateSeverity = useCallback(
    async (id: string, severity: number) => {
      const result = await trigger({ id, severity }).unwrap()
      setToast('📈 อัปเดต severity แล้ว')
      return result
    },
    [trigger],
  )

  return {
    updateSeverity,
    isLoading: state.isLoading,
    error: state.error,
    toast,
  }
}
