import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useLogIntakeMutation } from '../../../shared/api/api'
import type { LogIntakeRequest } from '../../../shared/api/healthTypes'

/**
 * Records a drug intake then surfaces a green toast + bounces the user
 * back to the Active Episode page. The bounce is the canonical happy
 * path: after "I took a pill" the user wants to see the new intake
 * pinned on the active screen, not stay on Take Medication.
 *
 * Callers can opt out of the bounce by passing `navigateBack: false`
 * (e.g., if they want to take another dose without leaving the page).
 */
export interface UseLogIntakeOptions {
  navigateBack?: boolean
}

export function useLogIntake({ navigateBack = true }: UseLogIntakeOptions = {}) {
  const navigate = useNavigate()
  const [trigger, state] = useLogIntakeMutation()
  const [toast, setToast] = useState<string | null>(null)

  useEffect(() => {
    if (!toast) return
    const t = window.setTimeout(() => setToast(null), 2200)
    return () => window.clearTimeout(t)
  }, [toast])

  const logIntake = useCallback(
    async (req: LogIntakeRequest, displayName?: string) => {
      const result = await trigger(req).unwrap()
      const label = displayName ?? result.drugName
      const doseSuffix = req.doseAmount > 1 ? ` ×${req.doseAmount}` : ''
      setToast(`✅ บันทึก ${label}${doseSuffix}`)
      if (navigateBack && req.symptomEpisodeId) {
        // Give the toast a beat to render before swapping routes.
        window.setTimeout(() => {
          navigate(`/health/active/${req.symptomEpisodeId}`)
        }, 600)
      }
      return result
    },
    [trigger, navigate, navigateBack],
  )

  return {
    logIntake,
    isLoading: state.isLoading,
    error: state.error,
    toast,
  }
}
