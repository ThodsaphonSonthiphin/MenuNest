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

/** Sun — leads the UV badge in a weather chip. */
export function SunIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <circle cx="12" cy="12" r="4.2" />
      <path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M19.1 4.9l-1.4 1.4M6.3 17.7l-1.4 1.4" />
    </svg>
  )
}

/** Thermometer — leads the feels-like alert pill on the itinerary stop card. */
export function ThermoIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M14 14.76V5a2 2 0 1 0-4 0v9.76a4 4 0 1 0 4 0z" />
    </svg>
  )
}

/** Clock — leads the hourly-planner entry/header. */
export function ClockIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </svg>
  )
}

/** Moon — the "coolest nighttime" quick action + night accent (issue #46 planner). */
export function MoonIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M20 14.5A8 8 0 0 1 9.5 4 7 7 0 1 0 20 14.5z" />
    </svg>
  )
}

/** Chevron-right — trailing affordance on the hourly-planner entry pill. */
export function ChevronRightIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M9 6l6 6-6 6" />
    </svg>
  )
}

/** Power — leads the "turns off current-time-start" note in the retiming preview. */
export function PowerIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M18.4 5.6a9 9 0 1 1-12.8 0" />
      <path d="M12 2v8" />
    </svg>
  )
}

/** Alert triangle — leads the whole-trip-shift warning in the retiming preview. */
export function AlertIcon({className}: IconProps) {
  return (
    <svg {...base} className={className}>
      <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" />
      <path d="M12 9v4M12 17h.01" />
    </svg>
  )
}
