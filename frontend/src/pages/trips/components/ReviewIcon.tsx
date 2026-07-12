// frontend/src/pages/trips/components/ReviewIcon.tsx
// Video/play glyph for the per-Stop review affordance. Colour from currentColor,
// size from the parent CSS (.stop-review-btn svg).
export function ReviewIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" focusable="false">
      <rect x="2.5" y="6" width="14" height="12" rx="2.5" />
      <path d="M16.5 10l5-3v10l-5-3z" fill="currentColor" stroke="none" />
    </svg>
  )
}
