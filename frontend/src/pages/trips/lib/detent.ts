// frontend/src/pages/trips/lib/detent.ts
//
// The Plan sheet's resting heights (menunest-234). Pure arithmetic, kept out of the
// component because the repo's vitest runs in `environment: 'node'` — a rule that lives
// inside a component is a rule no test can reach (CLAUDE.md).

/** One of the Plan sheet's three resting heights. CONTEXT.md calls this a **Detent**. */
export type Detent = 'summary' | 'half' | 'full'

/** Cycle order for the handle tap (Material 3's rule). */
export const DETENT_ORDER: readonly Detent[] = ['summary', 'half', 'full'] as const

/** Fractions of the viewport for the two dragged-open detents (spec §4.1). */
export const HALF_FRACTION = 0.55
export const FULL_FRACTION = 0.85

/**
 * Space the sheet must leave above itself at the `full` detent so the floating top bar
 * and the Day chips are never covered (spec §4.2: "the sheet never covers the floating
 * top bar; full stops below it").
 */
export const TOP_CHROME_PX = 96

/** Tapping the handle cycles summary → half → full → summary. */
export function nextDetent(d: Detent): Detent {
  const i = DETENT_ORDER.indexOf(d)
  return DETENT_ORDER[(i + 1) % DETENT_ORDER.length]
}

/**
 * The Thai label for the handle's accessible name. It names the state the tap moves TO,
 * not the state the sheet is in — a button is named by what it does.
 */
export function nextDetentLabel(d: Detent): string {
  switch (nextDetent(d)) {
    case 'half':
      return 'ขยายแผ่นแผนเป็นครึ่งจอ'
    case 'full':
      return 'ขยายแผ่นแผนเต็มจอ'
    default:
      return 'ย่อแผ่นแผนเหลือสรุป'
  }
}

/**
 * Resolved pixel height of a detent.
 *
 * `summaryHeightPx` is MEASURED (the header's real height), not guessed, because the
 * summary row's height depends on the Thai text and the safe-area inset. `half` and
 * `full` are viewport fractions, both clamped:
 *   - never below the summary height — a "taller" detent that is shorter than the one
 *     below it would make the drag cycle jump backwards;
 *   - never past `viewportHeightPx - TOP_CHROME_PX`, so the top bar stays reachable.
 */
export function detentHeight(d: Detent, viewportHeightPx: number, summaryHeightPx: number): number {
  const summary = Math.max(0, summaryHeightPx)
  if (d === 'summary') return summary
  const ceiling = Math.max(summary, viewportHeightPx - TOP_CHROME_PX)
  const raw = viewportHeightPx * (d === 'half' ? HALF_FRACTION : FULL_FRACTION)
  return Math.min(ceiling, Math.max(summary, raw))
}

/**
 * The detent a drag release settles on: the one whose height is closest to where the
 * user let go. Ties settle on the SHORTER detent (more map visible) — releasing exactly
 * between two rests should not steal map area the user did not ask for.
 */
export function nearestDetent(heightPx: number, viewportHeightPx: number, summaryHeightPx: number): Detent {
  let best: Detent = 'summary'
  let bestGap = Number.POSITIVE_INFINITY
  for (const d of DETENT_ORDER) {
    const gap = Math.abs(detentHeight(d, viewportHeightPx, summaryHeightPx) - heightPx)
    if (gap < bestGap) {
      bestGap = gap
      best = d
    }
  }
  return best
}
