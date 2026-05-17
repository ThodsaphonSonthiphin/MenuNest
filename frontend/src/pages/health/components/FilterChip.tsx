/**
 * Single toggle chip used by the History filter bar. Lighter chrome than
 * the form-style `ChipGroup` so a filter row can pack 5-6 chips on a
 * 360px screen.
 *
 * Mock: docs/mocks/patient-history-mock.html (left phone, filter bar).
 */
export interface FilterChipProps {
  label: string
  active: boolean
  onClick: () => void
}

export function FilterChip({ label, active, onClick }: FilterChipProps) {
  return (
    <button
      type="button"
      className={`health-filter-chip${active ? ' health-filter-chip--active' : ''}`}
      onClick={onClick}
      aria-pressed={active}
    >
      {label}
    </button>
  )
}
