import { useMemo, useState } from 'react'
import { useListEpisodesQuery } from '../../../shared/api/api'
import type {
  EpisodeDto,
  ListEpisodesQueryArgs,
} from '../../../shared/api/healthTypes'

/**
 * Drives the History page list. Holds local filter state, derives the
 * RTK Query args, and buckets the result by date so the page can render
 * "วันนี้ / เมื่อวาน / 13 พ.ค. ..." sections without any extra
 * grouping logic in the view.
 */
export interface EpisodeHistoryFilter {
  /** Date range preset. */
  rangePreset: 'last30' | 'all' | 'last7'
  symptomId: string | null
  onlyResolved: boolean
  onlyFailed: boolean
  /** Period-only filter (client-side: backend doesn't have a flag). */
  periodOnly: boolean
  /** Aura-only filter (client-side: not in the list payload yet, used as visual filter). */
  auraOnly: boolean
  /** Free-text search applied client-side over `symptomName`. */
  search: string
}

const DEFAULT_FILTER: EpisodeHistoryFilter = {
  rangePreset: 'last30',
  symptomId: null,
  onlyResolved: false,
  onlyFailed: false,
  periodOnly: false,
  auraOnly: false,
  search: '',
}

function toIsoDate(d: Date): string {
  const yyyy = d.getFullYear()
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${yyyy}-${mm}-${dd}`
}

function startOfDay(d: Date): Date {
  const r = new Date(d)
  r.setHours(0, 0, 0, 0)
  return r
}

function sameDay(a: Date, b: Date): boolean {
  return (
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate()
  )
}

const THAI_MONTH = [
  'ม.ค.',
  'ก.พ.',
  'มี.ค.',
  'เม.ย.',
  'พ.ค.',
  'มิ.ย.',
  'ก.ค.',
  'ส.ค.',
  'ก.ย.',
  'ต.ค.',
  'พ.ย.',
  'ธ.ค.',
]

function thaiDateLabel(d: Date, today: Date): string {
  if (sameDay(d, today)) {
    return `วันนี้ • ${d.getDate()} ${THAI_MONTH[d.getMonth()]}`
  }
  const yesterday = new Date(today)
  yesterday.setDate(yesterday.getDate() - 1)
  if (sameDay(d, yesterday)) {
    return `เมื่อวาน • ${d.getDate()} ${THAI_MONTH[d.getMonth()]}`
  }
  return `${d.getDate()} ${THAI_MONTH[d.getMonth()]}`
}

export interface EpisodeDateGroup {
  dateKey: string
  label: string
  episodes: EpisodeDto[]
}

export interface UseEpisodeHistoryResult {
  filter: EpisodeHistoryFilter
  setFilter: (next: Partial<EpisodeHistoryFilter>) => void
  resetFilter: () => void
  groups: EpisodeDateGroup[]
  totalCount: number
  isLoading: boolean
  isError: boolean
}

export function useEpisodeHistory(): UseEpisodeHistoryResult {
  const [filter, setFilterState] = useState<EpisodeHistoryFilter>(DEFAULT_FILTER)

  // Translate filter state into RTK Query args. Date range is computed
  // here so the API cache key changes when the preset flips.
  const queryArgs = useMemo<ListEpisodesQueryArgs>(() => {
    const today = startOfDay(new Date())
    const args: ListEpisodesQueryArgs = {}
    if (filter.rangePreset === 'last7') {
      const from = new Date(today)
      from.setDate(from.getDate() - 6)
      args.from = toIsoDate(from)
      args.to = toIsoDate(today)
    } else if (filter.rangePreset === 'last30') {
      const from = new Date(today)
      from.setDate(from.getDate() - 29)
      args.from = toIsoDate(from)
      args.to = toIsoDate(today)
    }
    if (filter.symptomId) args.symptomId = filter.symptomId
    if (filter.onlyResolved) args.onlyResolved = true
    if (filter.onlyFailed) args.onlyFailed = true
    return args
  }, [filter])

  const { data, isLoading, isError } = useListEpisodesQuery(queryArgs)

  const groups = useMemo<EpisodeDateGroup[]>(() => {
    if (!data || data.length === 0) return []
    const searchTerm = filter.search.trim().toLowerCase()

    // Apply client-side filters that aren't on the backend.
    const filtered = data.filter((ep) => {
      if (filter.periodOnly && !ep.isOnPeriod) return false
      // auraOnly: the list DTO doesn't include hasAura. We could fetch
      // detail per episode but that's expensive — Phase 1 we just skip
      // the filter if user toggles it and re-uses the default list.
      // (The mock chip stays togglable for symmetry with period.)
      if (filter.auraOnly) {
        // We don't have aura on the list DTO. Hide rows we *know* are
        // not aura-eligible: noDrugTaken+failed rows are unlikely aura
        // candidates. Without detail data we can't filter more precisely;
        // we let everything through so the user still sees something.
        // No-op until backend exposes hasAura on the list.
      }
      if (searchTerm) {
        const hay = `${ep.symptomName} ${ep.firstDrugName ?? ''}`.toLowerCase()
        if (!hay.includes(searchTerm)) return false
      }
      return true
    })

    // Sort newest first then bucket by local date.
    const sorted = [...filtered].sort(
      (a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime(),
    )

    const today = startOfDay(new Date())
    const map = new Map<string, EpisodeDateGroup>()
    for (const ep of sorted) {
      const localDate = startOfDay(new Date(ep.startedAt))
      const key = toIsoDate(localDate)
      let entry = map.get(key)
      if (!entry) {
        entry = {
          dateKey: key,
          label: thaiDateLabel(localDate, today),
          episodes: [],
        }
        map.set(key, entry)
      }
      entry.episodes.push(ep)
    }
    return Array.from(map.values())
  }, [data, filter])

  const totalCount = useMemo(
    () => groups.reduce((sum, g) => sum + g.episodes.length, 0),
    [groups],
  )

  const setFilter = (next: Partial<EpisodeHistoryFilter>) =>
    setFilterState((curr) => ({ ...curr, ...next }))
  const resetFilter = () => setFilterState(DEFAULT_FILTER)

  return { filter, setFilter, resetFilter, groups, totalCount, isLoading, isError }
}
