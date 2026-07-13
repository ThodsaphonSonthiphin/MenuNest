// frontend/src/pages/trips/components/ItineraryStopCard.tsx
import {useEffect, useRef, useState} from 'react'
import {createPortal} from 'react-dom'
import {useSortable} from '@dnd-kit/sortable'
import {CSS} from '@dnd-kit/utilities'
import type {TripPlaceDto, WeatherReadingDto} from '../../../shared/api/api'
import type {FlagReason, FlagSeverity, StopFlag, TimingFlag} from '../hooks/useSchedule'
import {catEmoji} from '../placeCategory'
import {flagText} from '../timingFlag'
import {NavIcon} from './NavIcon'
import {ReviewIcon} from './ReviewIcon'
import {reviewHost, reviewLabel} from '../lib/reviewLinks'
import {ClockIcon, LockIcon, MoonIcon, CheckIcon} from './FlagIcons'
import {GripIcon} from './TripFormIcons'
import {WeatherChip} from './WeatherChip'
import {formatDurationMinutes} from '../utils/time'

// Reason → icon component. `typeof LockIcon` avoids naming the JSX namespace.
const REASON_ICON: Record<FlagReason, typeof LockIcon> = {
  overflow: MoonIcon,
  closed: LockIcon,
  'off-window': ClockIcon,
}
// Severity → CSS class. NEVER interpolate the raw severity string (enum ≠ class name).
const CARD_CLASS: Record<FlagSeverity, string> = {problem: 'bad', suggestion: 'warn'}

function FlagNote({flag}: {flag: TimingFlag}) {
  const Icon = REASON_ICON[flag.reason]
  const {reasonLine, fixLine} = flagText(flag)
  return (
    <div className={`flag-note${flag.severity === 'problem' ? ' bad' : ''}`}>
      <Icon />
      <span><b>{reasonLine}</b> <span className="fix">{fixLine}</span></span>
    </div>
  )
}

export function ItineraryStopCard({
  id,
  place,
  arrival,
  depart,
  dwell,
  flag,
  onEdit,
  navUrl,
  onNavigate,
  nowReading,
  arrivalReading,
  weatherLoading = false,
  isVisited,
  onToggleVisited,
}: {
  id: string
  place: TripPlaceDto
  arrival: string
  depart: string
  dwell: number
  flag: StopFlag
  onEdit: () => void
  navUrl: string | null
  onNavigate?: () => void
  nowReading?: WeatherReadingDto
  arrivalReading?: WeatherReadingDto
  weatherLoading?: boolean
  isVisited: boolean
  onToggleVisited: (next: boolean) => void
}) {
  const {attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging} =
    useSortable({id})
  const style = {transform: CSS.Transform.toString(transform), transition}

  const links = place.reviewLinks ?? []
  const [reviewOpen, setReviewOpen] = useState(false)
  const reviewRef = useRef<HTMLDivElement>(null)
  const reviewBtnRef = useRef<HTMLButtonElement>(null)
  const reviewMenuRef = useRef<HTMLDivElement>(null)
  const [menuPos, setMenuPos] = useState<{top: number; right: number} | null>(null)
  useEffect(() => {
    if (!reviewOpen) return
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node
      if (!reviewRef.current?.contains(t) && !reviewMenuRef.current?.contains(t)) setReviewOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setReviewOpen(false)
    const close = () => setReviewOpen(false)
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    window.addEventListener('scroll', close, true)
    window.addEventListener('resize', close)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('scroll', close, true)
      window.removeEventListener('resize', close)
    }
  }, [reviewOpen])

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`stop-card${flag ? ' ' + CARD_CLASS[flag.severity] : ''}${isVisited ? ' visited' : ''}${isDragging ? ' dragging' : ''}`}
      data-testid="itin-stop-card"
      data-stop-id={id}
    >
      <label className="stop-check">
        <input
          type="checkbox"
          checked={isVisited}
          onChange={(e) => onToggleVisited(e.target.checked)}
          aria-label={`มาแล้ว: ${place.name}`}
        />
      </label>
      <div className="stop-rail">
        <div className="stop-arr">{arrival}</div>
        <div className="stop-dep">→{depart}</div>
      </div>
      <button className="stop-body" onClick={onEdit}>
        <div className="stop-name">{catEmoji(place.category)} {place.name}</div>
        <div className="stop-chips">
          {isVisited && <span className="chip visited"><CheckIcon />มาแล้ว</span>}
          <span className="chip dwell">⏱ อยู่ {formatDurationMinutes(dwell)}</span>
          <WeatherChip kind="now" reading={nowReading} isLoading={weatherLoading} />
          <WeatherChip kind="arr" reading={arrivalReading} isLoading={weatherLoading} />
        </div>
        {flag && <FlagNote flag={flag} />}
      </button>
      {links.length === 1 && (
        <a
          className="stop-review-btn"
          href={links[0].url}
          target="_blank"
          rel="noopener noreferrer"
          aria-label="ดูรีวิว"
          onClick={(e) => e.stopPropagation()}
        >
          <ReviewIcon />
        </a>
      )}
      {links.length >= 2 && (
        <div className="stop-review-wrap" ref={reviewRef}>
          <button
            ref={reviewBtnRef}
            type="button"
            className="stop-review-btn"
            aria-label={`ดูรีวิว (${links.length})`}
            aria-expanded={reviewOpen}
            onClick={(e) => {
              e.stopPropagation()
              setReviewOpen((v) => {
                const next = !v
                if (next && reviewBtnRef.current) {
                  const r = reviewBtnRef.current.getBoundingClientRect()
                  setMenuPos({top: r.bottom + 4, right: window.innerWidth - r.right})
                }
                return next
              })
            }}
          >
            <ReviewIcon />
            <span className="rv-count">{links.length}</span>
          </button>
        </div>
      )}
      {reviewOpen &&
        menuPos &&
        createPortal(
          <div
            className="rv-menu"
            ref={reviewMenuRef}
            role="menu"
            style={{position: 'fixed', top: menuPos.top, right: menuPos.right, zIndex: 50}}
          >
            <div className="rv-menu-title">รีวิว</div>
            {links.map((l, i) => (
              <a
                key={l.url + i}
                href={l.url}
                target="_blank"
                rel="noopener noreferrer"
                role="menuitem"
                onClick={() => setReviewOpen(false)}
              >
                <ReviewIcon />
                <span className="rv-label">{reviewLabel(l, i)}</span>
                <span className="host">{reviewHost(l.url)}</span>
              </a>
            ))}
          </div>,
          document.body,
        )}
      {navUrl ? (
        <a
          className="stop-nav"
          href={navUrl}
          target="_blank"
          rel="noopener noreferrer"
          aria-label="นำทาง"
          onClick={(e) => {
            e.stopPropagation()
            onNavigate?.()
          }}
        >
          <NavIcon />
        </a>
      ) : (
        <span className="stop-nav" role="img" aria-label="ไม่มีพิกัดสำหรับนำทาง" aria-disabled="true">
          <NavIcon />
        </span>
      )}
      <button
        ref={setActivatorNodeRef}
        type="button"
        className="stop-drag-handle"
        aria-label="ลากเพื่อจัดลำดับ"
        data-testid="stop-drag-handle"
        {...attributes}
        {...listeners}
      >
        <GripIcon />
      </button>
    </div>
  )
}
