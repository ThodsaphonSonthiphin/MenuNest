/**
 * Generic chip selector used across the Quick Log form. Supports both
 * single-select (location, quality, impact) and multi-select (triggers,
 * associated symptoms). Color variants come from a CSS class on each
 * chip — see `.health-chip--symptom/--period/--aura` in health.css.
 *
 * The component is split into two exported variants — `ChipGroup`
 * (single-select) and `ChipGroupMulti` (multi-select) — because a
 * single component with a generic discriminated union narrows poorly
 * through React.FC's PropsWithChildren wrapping. Splitting keeps each
 * call site fully typed without callers ever asserting back to `any`.
 */
export type ChipColor = 'default' | 'symptom' | 'period' | 'aura'

export interface ChipOption<T> {
  id: T
  label: string
  color?: ChipColor
}

interface ChipChrome<T> {
  options: ChipOption<T>[]
  className?: string
}

export interface ChipGroupProps<T> extends ChipChrome<T> {
  value: T | null
  onChange: (next: T | null) => void
}

export interface ChipGroupMultiProps<T> extends ChipChrome<T> {
  value: T[]
  onChange: (next: T[]) => void
}

function chipClassName<T>(opt: ChipOption<T>, active: boolean): string {
  const colorClass =
    opt.color && opt.color !== 'default' ? ` health-chip--${opt.color}` : ''
  return `health-chip${active ? ' health-chip--active' : ''}${colorClass}`
}

export function ChipGroup<T extends string | number>(props: ChipGroupProps<T>) {
  const { options, value, onChange, className } = props
  return (
    <div className={`health-chip-group ${className ?? ''}`.trim()}>
      {options.map((opt) => {
        const active = value === opt.id
        return (
          <button
            type="button"
            key={String(opt.id)}
            className={chipClassName(opt, active)}
            // Tap on active = clear, so a stray tap can be undone with
            // a second tap rather than forcing a "none" chip everywhere.
            onClick={() => onChange(active ? null : opt.id)}
            aria-pressed={active}
          >
            {opt.label}
          </button>
        )
      })}
    </div>
  )
}

export function ChipGroupMulti<T extends string | number>(
  props: ChipGroupMultiProps<T>,
) {
  const { options, value, onChange, className } = props
  return (
    <div className={`health-chip-group ${className ?? ''}`.trim()}>
      {options.map((opt) => {
        const active = value.includes(opt.id)
        return (
          <button
            type="button"
            key={String(opt.id)}
            className={chipClassName(opt, active)}
            onClick={() =>
              onChange(active ? value.filter((v) => v !== opt.id) : [...value, opt.id])
            }
            aria-pressed={active}
          >
            {opt.label}
          </button>
        )
      })}
    </div>
  )
}
