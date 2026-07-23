// frontend/src/pages/trips/components/TripFormIcons.tsx
// Inline SVG icons for the Create-Trip dialog. Sized via `1em` (set font-size on
// the container) and coloured via `currentColor`, matching the NavIcon pattern.
// Project rule: menunest UI icons are SVG components, never emoji glyphs.

type IconProps = {className?: string}

const base = {
  viewBox: '0 0 24 24',
  width: '1em',
  height: '1em',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
  focusable: false,
}

/** Suitcase — dialog header badge. */
export function SuitcaseIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <rect x="3" y="7" width="18" height="13" rx="2.5" />
      <path d="M8 7V5.5A2.5 2.5 0 0 1 10.5 3h3A2.5 2.5 0 0 1 16 5.5V7" />
      <path d="M12 11v5" />
    </svg>
  )
}

/** Location pin — destination field lead icon. */
export function MapPinIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M12 21s-6.5-5.7-6.5-10.5a6.5 6.5 0 1 1 13 0C18.5 15.3 12 21 12 21z" />
      <circle cx="12" cy="10.5" r="2.4" />
    </svg>
  )
}

/** Rightward arrow — end-date summary pill. */
export function ArrowRightIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M4 12h15" />
      <path d="M13 6l6 6-6 6" />
    </svg>
  )
}

/** Car — Drive travel mode. */
export function CarIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M5 13l1.6-4.3A2 2 0 0 1 8.5 7.4h7A2 2 0 0 1 17.4 8.7L19 13" />
      <path d="M3.5 13h17v4h-17z" />
      <circle cx="7.5" cy="17" r="1.6" />
      <circle cx="16.5" cy="17" r="1.6" />
    </svg>
  )
}

/** Bus — Transit travel mode. */
export function TransitIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <rect x="5" y="4" width="14" height="13" rx="2.5" />
      <path d="M5 12h14" />
      <circle cx="8.5" cy="14.5" r="1" />
      <circle cx="15.5" cy="14.5" r="1" />
      <path d="M7.5 20l1.5-3M16.5 20l-1.5-3" />
    </svg>
  )
}

/** Walking figure — Walk travel mode. */
export function WalkIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <circle cx="13" cy="4.5" r="1.7" />
      <path d="M12.5 8l-2 3.6L12 14v6" />
      <path d="M12 12.5l3 1 1.8 2.8" />
      <path d="M10.5 11.6L8 13.5" />
      <path d="M12 14l-2 6" />
    </svg>
  )
}

/** Plus — stepper increment / create-trip button. */
export function PlusIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M12 5v14M5 12h14" />
    </svg>
  )
}

/** Minus — stepper decrement. */
export function MinusIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M5 12h14" />
    </svg>
  )
}

/** Chevron up — collapse the itinerary map band. */
export function ChevronUpIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M6 15l6-6 6 6" />
    </svg>
  )
}

/** Chevron down — expand the collapsed itinerary map band. */
export function ChevronDownIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M6 9l6 6 6-6" />
    </svg>
  )
}

/** Chevron right — trailing "opens detail" affordance on the compact stop card (issue #34). */
export function ChevronRightIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M9 18l6-6-6-6" />
    </svg>
  )
}

/** Folded map — lead glyph on the collapsed "show route map" strip. */
export function MapRouteIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M9 4 4 6v14l5-2 6 2 5-2V4l-5 2-6-2z" />
      <path d="M9 4v14M15 6v14" />
    </svg>
  )
}

/** Grip dots — drag handle for reordering Stops (ADR-044). Uses filled dots, not the stroked `base`. */
export function GripIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="currentColor" aria-hidden focusable={false} className={className}>
      <circle cx="9" cy="5" r="1.6" />
      <circle cx="15" cy="5" r="1.6" />
      <circle cx="9" cy="12" r="1.6" />
      <circle cx="15" cy="12" r="1.6" />
      <circle cx="9" cy="19" r="1.6" />
      <circle cx="15" cy="19" r="1.6" />
    </svg>
  )
}

/** Repeat / recurring — daily-trip badge + section (issue #49). */
export function RepeatIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M17 2l4 4-4 4" />
      <path d="M3 11V9a4 4 0 0 1 4-4h14" />
      <path d="M7 22l-4-4 4-4" />
      <path d="M21 13v2a4 4 0 0 1-4 4H3" />
    </svg>
  )
}
