// frontend/src/pages/trips/components/ChecklistIcon.tsx
// Clipboard-check glyph for the Place checklist section head ("สิ่งที่ต้องเตรียม").
// Inline SVG, never emoji (trips convention). Colour from currentColor, size from
// the parent CSS (.se-sec-head svg).
export function ChecklistIcon() {
  return (
    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <path d="M9 4h6a1 1 0 0 1 1 1v1H8V5a1 1 0 0 1 1-1z" />
      <path d="M8 5H6a1 1 0 0 0-1 1v13a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V6a1 1 0 0 0-1-1h-2" />
      <path d="M9 13l2 2 4-4" />
    </svg>
  )
}