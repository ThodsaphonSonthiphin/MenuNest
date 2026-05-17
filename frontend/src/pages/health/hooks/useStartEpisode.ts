import { useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { useStartEpisodeMutation } from '../../../shared/api/api'
import type {
  EpisodeDto,
  StartEpisodeRequest,
} from '../../../shared/api/healthTypes'

/**
 * Thin wrapper around the start-episode mutation. On success, navigates
 * to the Active Episode screen for the newly created episode — that's
 * the canonical post-create destination per the daily-flow brief
 * (Quick Log → Active Episode).
 *
 * Callers receive the typed `EpisodeDto` so they can also use it
 * directly (e.g. for analytics, in-page transitions). Errors propagate
 * unchanged so the form can render a message.
 */
export function useStartEpisode() {
  const navigate = useNavigate()
  const [trigger, state] = useStartEpisodeMutation()

  const startEpisode = useCallback(
    async (args: StartEpisodeRequest): Promise<EpisodeDto> => {
      const result = await trigger(args).unwrap()
      navigate(`/health/active/${result.id}`)
      return result
    },
    [trigger, navigate],
  )

  return {
    startEpisode,
    isLoading: state.isLoading,
    error: state.error,
  }
}
