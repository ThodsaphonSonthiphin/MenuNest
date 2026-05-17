/**
 * Small inline pill that renders `severity/10` color-coded by intensity.
 *  1-3 → yellow (mild)
 *  4-6 → orange (moderate)
 *  7+  → red    (severe)
 *
 * Used in History rows and the Active banner meta line.
 */
export interface SeverityPillProps {
  value: number
  max?: number
}

export function SeverityPill({ value, max = 10 }: SeverityPillProps) {
  let modifier = 'health-severity-pill--mild'
  if (value >= 7) modifier = 'health-severity-pill--severe'
  else if (value >= 4) modifier = 'health-severity-pill--moderate'

  return (
    <span className={`health-severity-pill ${modifier}`}>
      {value}/{max}
    </span>
  )
}
