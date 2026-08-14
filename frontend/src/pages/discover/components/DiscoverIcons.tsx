// frontend/src/pages/discover/components/DiscoverIcons.tsx
// Inline-SVG icon set for the Discover screen. Path data is lifted verbatim from
// the <symbol> defs in docs/mocks/place-discovery-mock.html so the shipped UI and
// the approved mock cannot drift; the mock's own footer states "ไอคอน = inline SVG
// ไม่ใช่ emoji", which is also the project-wide rule.
//
// Category art is exported twice on purpose:
//   - CAT_ICON_PATHS  → raw path markup, because DiscoverMap builds its
//     AdvancedMarker content imperatively as a DOM node (no React render there).
//   - <CategoryIcon>  → the same art for React call sites (list rows, detail head).
// One source, two consumers, so a pin and its list row can never show different art.

interface IconProps {
  className?: string
}

const STROKE = {
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
}

/** Raw inner markup per category, sized for a 24×24 viewBox. */
export const CAT_ICON_PATHS: Record<string, string> = {
  See: '<path d="M3 8h4l2-2.5h6L17 8h4v11H3z"/><circle cx="12" cy="13" r="3.2"/>',
  Eat: '<path d="M4 3v6a2 2 0 0 0 2 2h0a2 2 0 0 0 2-2V3M6 11v10M18 3c-1.7 0-3 2-3 5s1 4 3 4v9"/>',
  Cafe: '<path d="M4 8h13v5a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5z"/><path d="M17 9h2a2 2 0 0 1 0 5h-2"/>',
  Stay: '<path d="M3 18V7M3 12h18v6M21 18v-4a2 2 0 0 0-2-2M7 12V9h5v3"/>',
  Shop: '<path d="M12 2a7 7 0 0 0-7 7c0 5 7 13 7 13s7-8 7-13a7 7 0 0 0-7-7zm0 9.5A2.5 2.5 0 1 1 12 6a2.5 2.5 0 0 1 0 5.5z"/>',
  Other: '<path d="M12 2a7 7 0 0 0-7 7c0 5 7 13 7 13s7-8 7-13a7 7 0 0 0-7-7zm0 9.5A2.5 2.5 0 1 1 12 6a2.5 2.5 0 0 1 0 5.5z"/>',
}

/**
 * A complete <svg> string for the map pin. `Shop`/`Other` use the solid pin glyph,
 * which needs fill rather than stroke — hence the per-category fill/stroke switch.
 */
export function categorySvgMarkup(category: string, size: number, rotateDeg = 0): string {
  const solid = category === 'Shop' || category === 'Other'
  const paint = solid
    ? 'fill="currentColor"'
    : 'fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"'
  const spin = rotateDeg ? ` style="transform:rotate(${rotateDeg}deg)"` : ''
  return (
    `<svg viewBox="0 0 24 24" width="${size}" height="${size}" ${paint}${spin} aria-hidden="true">` +
    `${CAT_ICON_PATHS[category] ?? CAT_ICON_PATHS.Other}</svg>`
  )
}

export function CategoryIcon({category, className}: IconProps & {category: string}) {
  const solid = category === 'Shop' || category === 'Other'
  const paint = solid ? {fill: 'currentColor'} : STROKE
  return (
    <svg
      viewBox="0 0 24 24"
      className={className}
      aria-hidden="true"
      {...paint}
      dangerouslySetInnerHTML={{__html: CAT_ICON_PATHS[category] ?? CAT_ICON_PATHS.Other}}
    />
  )
}

export function SearchIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE} strokeWidth={2.2}>
      <circle cx="11" cy="11" r="7" />
      <path d="M21 21l-4.3-4.3" />
    </svg>
  )
}

export function LocateIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE}>
      <circle cx="12" cy="12" r="3.5" />
      <path d="M12 2v3M12 19v3M2 12h3M19 12h3" />
    </svg>
  )
}

export function ChevronIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE} strokeWidth={2.5}>
      <path d="M6 9l6 6 6-6" />
    </svg>
  )
}

/** Replaces the literal "✕" glyph the mock's frame-7 caption calls out. */
export function CloseIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE} strokeWidth={2.2}>
      <path d="M6 6l12 12M18 6L6 18" />
    </svg>
  )
}

export function NavArrowIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" fill="currentColor">
      <path d="M3 11l18-8-8 18-2-8-8-2z" />
    </svg>
  )
}

export function TripIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE}>
      <rect x="4" y="7" width="16" height="13" rx="2" />
      <path d="M9 7V5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2" />
    </svg>
  )
}

export function CheckIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE} strokeWidth={2.6}>
      <path d="M20 6 9 17l-5-5" />
    </svg>
  )
}

export function PlusIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE} strokeWidth={2.4}>
      <path d="M12 5v14M5 12h14" />
    </svg>
  )
}

export function OpenIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE}>
      <path d="M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5" />
    </svg>
  )
}

export function SlidersIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE}>
      <path d="M4 6h10M18 6h2M4 12h2M10 12h10M4 18h8M16 18h4" />
      <circle cx="15" cy="6" r="2" />
      <circle cx="7" cy="12" r="2" />
      <circle cx="13" cy="18" r="2" />
    </svg>
  )
}

export function SunIcon({className}: IconProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" {...STROKE}>
      <circle cx="12" cy="12" r="4.2" />
      <path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M19.1 4.9l-1.4 1.4M6.3 17.7l-1.4 1.4" />
    </svg>
  )
}
