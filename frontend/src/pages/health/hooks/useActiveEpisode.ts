import { useGetEpisodeQuery } from '../../../shared/api/api'

/**
 * Detail-query wrapper for the Active Episode screen. Polls the server
 * every 30 seconds so the elapsed timer + drug progress + follow-up
 * countdown stay in sync without manual refresh. Pause polling when
 * `id` is missing (e.g. transient route state).
 */
export function useActiveEpisode(id: string | undefined) {
  return useGetEpisodeQuery(id ?? '', {
    skip: !id,
    pollingInterval: 30_000,
    refetchOnFocus: true,
    refetchOnReconnect: true,
  })
}
