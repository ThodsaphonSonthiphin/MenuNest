// frontend/src/pages/trips/components/WeatherIcons.tsx
// Inline SVG icons for the trip weather chips. Sized via `1em` (set font-size on
// the container) and coloured via `currentColor`, matching the TripFormIcons pattern.
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

/** Rain-drop — precedes the rain-probability % in a weather chip. */
export function RainDropIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M12 3s6 7 6 10.5A6 6 0 0 1 6 13.5C6 10 12 3 12 3z" />
    </svg>
  )
}

/** Slashed cloud — the "no weather data" chip glyph. */
export function NoWeatherIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M17.5 19a4.5 4.5 0 0 0 .3-9 6 6 0 0 0-11.4-1.5" />
      <path d="M4 4l16 16" />
    </svg>
  )
}
