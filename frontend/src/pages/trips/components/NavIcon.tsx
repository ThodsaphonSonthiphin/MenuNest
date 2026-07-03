// frontend/src/pages/trips/components/NavIcon.tsx
// Google-Maps-style navigation arrow. Colour comes from `currentColor`; size
// from the parent's CSS (.btn-day-nav svg / .stop-nav svg). Shared by the
// whole-day pill and the per-Stop button.
export function NavIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" focusable="false">
      <path d="M21.71 11.29l-9-9a1 1 0 0 0-1.42 0l-9 9a1 1 0 0 0 0 1.42l9 9a1 1 0 0 0 1.42 0l9-9a1 1 0 0 0 0-1.42zM14 14.5V12h-4v3H8v-4a1 1 0 0 1 1-1h5V7.5l3.5 3.5z" />
    </svg>
  )
}
