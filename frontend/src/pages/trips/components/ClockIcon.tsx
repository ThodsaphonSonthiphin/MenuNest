// frontend/src/pages/trips/components/ClockIcon.tsx
// Clock glyph for the best-time window rows (issue #38). Colour from currentColor
// (teal via .bt-clock), size from the parent CSS (.bt-clock svg).
export function ClockIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" focusable="false">
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </svg>
  )
}
