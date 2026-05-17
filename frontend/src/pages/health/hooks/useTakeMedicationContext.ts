import { useGetTakeMedicationContextQuery } from '../../../shared/api/api'

/**
 * Wraps the take-medication context endpoint with a 30-second poll. The
 * page needs live data because:
 *  - `activeDrugs` countdowns shrink in real time.
 *  - As an active drug crosses its `effectEndsAt`, the server moves it
 *    out of `activeDrugs` and into `takeableDrugs` (or `blockedDrugs`),
 *    so we want to pick that up without a manual refresh.
 *
 * Polling pauses automatically when the tab is hidden (RTK Query default).
 */
export function useTakeMedicationContext(episodeId: string | undefined) {
  return useGetTakeMedicationContextQuery(episodeId ?? '', {
    skip: !episodeId,
    pollingInterval: 30_000,
    refetchOnFocus: true,
    refetchOnReconnect: true,
  })
}
