/**
 * Vertical timeline of events on the Episode Detail screen. The component
 * is purely visual — callers compute the event list (see
 * `useEpisodeDetail`) so we can reuse the same chrome for any kind of
 * event stream later (e.g., doctor reports).
 *
 * Mock: docs/mocks/patient-history-mock.html (right phone, "timeline"
 * section).
 */
export type TimelineEvent =
  | {
      kind: 'symptom-start'
      time: string
      label: string
      note?: string | null
    }
  | {
      kind: 'intake'
      time: string
      label: string
      note?: string | null
    }
  | {
      kind: 'followup'
      time: string
      label: string
      note?: string | null
    }

export interface TimelineViewProps {
  events: TimelineEvent[]
}

function formatTimeOfDay(iso: string): string {
  return new Date(iso).toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function TimelineView({ events }: TimelineViewProps) {
  if (events.length === 0) {
    return (
      <div style={{ fontSize: 12, color: 'var(--hl-text-muted)' }}>
        ยังไม่มี timeline events
      </div>
    )
  }
  return (
    <div className="health-timeline">
      {events.map((evt, i) => (
        <div
          key={`${evt.kind}-${evt.time}-${i}`}
          className={`health-timeline-item health-timeline-item--${evt.kind}`}
        >
          <div className="health-timeline-time">{formatTimeOfDay(evt.time)}</div>
          <div className="health-timeline-event">{evt.label}</div>
          {evt.note && <div className="health-timeline-note">{evt.note}</div>}
        </div>
      ))}
    </div>
  )
}
