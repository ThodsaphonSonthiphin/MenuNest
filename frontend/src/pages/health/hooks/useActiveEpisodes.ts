import { useEffect } from 'react'
import { useGetActiveEpisodesQuery } from '../../../shared/api/api'

/**
 * Wraps `useGetActiveEpisodesQuery` with a 30-second auto-refetch that
 * pauses when the browser tab is in the background. We refetch on
 * window focus too, so opening the tab after a long pause shows fresh
 * data without waiting for the next interval tick.
 *
 * Returning the full RTK Query result keeps callers flexible — they can
 * surface `isLoading`/`error` if they want, or just consume `data`.
 */
export function useActiveEpisodes() {
  const query = useGetActiveEpisodesQuery(undefined, {
    pollingInterval: 30_000,
    refetchOnFocus: true,
    refetchOnReconnect: true,
  })

  // Reduce traffic when the tab is hidden: RTK Query's pollingInterval
  // is fixed once subscribed, so we manually refetch on visibilitychange
  // to catch up after returning from background.
  useEffect(() => {
    const onVis = () => {
      if (document.visibilityState === 'visible') {
        void query.refetch()
      }
    }
    document.addEventListener('visibilitychange', onVis)
    return () => document.removeEventListener('visibilitychange', onVis)
  }, [query])

  return query
}
