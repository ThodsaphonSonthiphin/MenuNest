import { useMemo } from 'react'
import { useGetEpisodeQuery } from '../../../shared/api/api'
import type {
  EpisodeDetailDto,
  EpisodeFollowUpDto,
} from '../../../shared/api/healthTypes'
import { PingResponse } from '../../../shared/api/healthTypes'
import type { TimelineEvent } from '../components/TimelineView'

/**
 * Builds the per-episode timeline shown on the Episode Detail page.
 *
 * We combine three event streams:
 *   1. `symptom-start` — synthetic event at `episode.startedAt`.
 *   2. `intake`         — one per recorded drug intake.
 *   3. `followup`       — only follow-ups the user actually answered.
 *      Pending pings would clutter past-episodes; resolved/improved/etc.
 *      responses are the useful clinical artifacts.
 *
 * Events are sorted ascending by time so the visual flows top-to-bottom
 * like a story.
 */
const PING_LABEL: Record<PingResponse, string> = {
  [PingResponse.Resolved]: '📊 follow-up: หายแล้ว 0/10 ✓',
  [PingResponse.Improved]: '📊 follow-up: ดีขึ้น',
  [PingResponse.Same]: '📊 follow-up: ยังเหมือนเดิม',
  [PingResponse.Worse]: '📊 follow-up: แย่ลง',
  [PingResponse.RetroResolved]: '📊 retro: หายแล้ว',
  [PingResponse.RetroUnknown]: '📊 retro: ไม่แน่ใจ',
}

function buildTimeline(detail: EpisodeDetailDto): TimelineEvent[] {
  const events: TimelineEvent[] = []

  // Symptom start (always first event)
  const startNoteParts: string[] = []
  if (detail.isOnPeriod) startNoteParts.push('ประจำเดือน')
  if (detail.hasAura) startNoteParts.push('aura')
  events.push({
    kind: 'symptom-start',
    time: detail.startedAt,
    label: `🤒 ${detail.symptomName} ${detail.severity}/10`,
    note: startNoteParts.length ? startNoteParts.join(' • ') : null,
  })

  for (const intake of detail.intakes ?? []) {
    const doseSuffix = intake.doseAmount > 1 ? ` ×${intake.doseAmount}` : ''
    events.push({
      kind: 'intake',
      time: intake.takenAt,
      label: `💊 ${intake.drugName} ${intake.doseStrength}${doseSuffix}`,
      note: `→ รักษา${detail.symptomName}`,
    })
  }

  for (const fu of (detail.followUps ?? []) as EpisodeFollowUpDto[]) {
    if (!fu.respondedAt || !fu.response) continue
    const label = PING_LABEL[fu.response] ?? '📊 follow-up'
    const note =
      fu.severityAtCheck != null ? `severity ${fu.severityAtCheck}/10` : null
    events.push({
      kind: 'followup',
      time: fu.respondedAt,
      label,
      note,
    })
  }

  return events.sort(
    (a, b) => new Date(a.time).getTime() - new Date(b.time).getTime(),
  )
}

export interface UseEpisodeDetailResult {
  detail: EpisodeDetailDto | undefined
  timelineEvents: TimelineEvent[]
  isLoading: boolean
  isError: boolean
  error: unknown
}

export function useEpisodeDetail(id: string | undefined): UseEpisodeDetailResult {
  const { data, isLoading, isError, error } = useGetEpisodeQuery(id ?? '', {
    skip: !id,
  })

  const timelineEvents = useMemo(
    () => (data ? buildTimeline(data) : []),
    [data],
  )

  return {
    detail: data,
    timelineEvents,
    isLoading,
    isError,
    error,
  }
}
