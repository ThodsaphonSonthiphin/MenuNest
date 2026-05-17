import { useMemo } from 'react'
import { useListEpisodesQuery } from '../../../shared/api/api'
import type {
  AssociatedSymptom,
  FunctionalImpact,
  SymptomLocation,
  SymptomQuality,
} from '../../../shared/api/healthTypes'

/**
 * Smart pre-fill source for Quick Log. Looks at the most recent episode
 * for the *same symptom* within the last 7 days and returns its
 * "habitual" attributes so the form can light up chips for the user to
 * confirm rather than re-pick.
 *
 * We fetch the last 30 days of episodes (cheap on the wire, naturally
 * cached by RTK Query) and filter client-side. The DTO is the list
 * (`EpisodeDto`) which doesn't carry `location`/`quality` directly —
 * those live on `EpisodeDetailDto`. For Phase 1 we therefore only
 * surface fields available on the list DTO (`triggerIds` etc are also
 * not on the list DTO). The richer pre-fill arrives once the backend
 * adds the missing fields or we batch a second detail call; for now
 * the hook returns `null` so the UI omits the "from last time" badge.
 *
 * NOTE: even though we return `null` today, keeping this hook in place
 * means downstream code already wires through the pre-fill path —
 * Task 14b/c can enrich the source without touching call sites.
 */
export interface LastEpisodeAttributes {
  location?: SymptomLocation | null
  quality?: SymptomQuality | null
  triggerIds?: string[] | null
  associatedSymptoms?: AssociatedSymptom[] | null
  functionalImpact?: FunctionalImpact | null
}

export function useLastEpisodeAttributes(symptomId?: string | null): LastEpisodeAttributes | null {
  // Window: today – 30 days. We only consume the most recent inside 7
  // days but cast the net wider so RTK Query can re-use the cache for
  // the History page once it lands.
  const from = useMemo(() => {
    const d = new Date()
    d.setDate(d.getDate() - 30)
    return d.toISOString().slice(0, 10)
  }, [])

  const { data: episodes } = useListEpisodesQuery({
    from,
    symptomId: symptomId ?? undefined,
  })

  return useMemo(() => {
    if (!episodes || !symptomId) return null
    const cutoff = Date.now() - 7 * 24 * 60 * 60 * 1000

    const candidate = episodes
      .filter((e) => e.symptomId === symptomId)
      .filter((e) => new Date(e.startedAt).getTime() >= cutoff)
      .sort(
        (a, b) =>
          new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime(),
      )[0]

    if (!candidate) return null

    // EpisodeDto (the list shape) does not include location/quality/etc.
    // Surface what we have. Future iterations can fan out a detail call
    // on this candidate id to populate the rich fields.
    return {} as LastEpisodeAttributes
  }, [episodes, symptomId])
}
