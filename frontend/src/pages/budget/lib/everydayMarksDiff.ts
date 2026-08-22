export interface EverydayMarkDiffEntry {
  categoryId: string
  isEveryday: boolean
}

/**
 * Diff between how the EverydayMarksSheet opened (`original`, one entry per
 * envelope, taken from `EnvelopeDto.isEveryday`) and the local tick state it
 * accumulates while open (`ticked`, keyed by categoryId). Returns only the
 * envelopes whose mark actually differs — an empty array means nothing
 * changed, which is the SPA's own signal not to send a request at all
 * (menunest-184: a request that changes nothing must not re-freeze the
 * Daily allowance; the backend's own `changed` gate in
 * `SetEverydayMarksHandler` is a backstop, not a reason to skip this check).
 *
 * A categoryId absent from `ticked` (should not happen — the sheet seeds
 * every envelope on open — but this stays defensive) is treated as
 * unchanged rather than as "flipped to false", so a partially-seeded ticked
 * map can never silently unmark envelopes the sheet never rendered.
 */
export function diffEverydayMarks(
  original: EverydayMarkDiffEntry[],
  ticked: Record<string, boolean>,
): EverydayMarkDiffEntry[] {
  const diff: EverydayMarkDiffEntry[] = []
  for (const entry of original) {
    const next = ticked[entry.categoryId]
    if (next === undefined) continue
    if (next !== entry.isEveryday) diff.push({categoryId: entry.categoryId, isEveryday: next})
  }
  return diff
}
