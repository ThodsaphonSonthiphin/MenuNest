import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useLogNoDrugMutation } from '../../../shared/api/api'
import type { NoDrugReason } from '../../../shared/api/healthTypes'

/**
 * Records a "didn't take any drug" event for an active episode and
 * bounces back to the Active Episode page so the user can keep
 * managing the attack. Mirrors `useLogIntake` for the alternative path.
 */
export function useLogNoDrug() {
  const navigate = useNavigate()
  const [trigger, state] = useLogNoDrugMutation()
  const [toast, setToast] = useState<string | null>(null)

  useEffect(() => {
    if (!toast) return
    const t = window.setTimeout(() => setToast(null), 2200)
    return () => window.clearTimeout(t)
  }, [toast])

  const logNoDrug = useCallback(
    async (episodeId: string, reason: NoDrugReason) => {
      await trigger({ episodeId, reason }).unwrap()
      setToast('📝 บันทึก "ไม่ได้กินยา"')
      window.setTimeout(() => {
        navigate(`/health/active/${episodeId}`)
      }, 600)
    },
    [trigger, navigate],
  )

  return {
    logNoDrug,
    isLoading: state.isLoading,
    error: state.error,
    toast,
  }
}
