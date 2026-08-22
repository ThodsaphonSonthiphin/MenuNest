import {formatTHB} from './formatTHB'

/**
 * menunest-186: the pace line is the only part of the daily-allowance card
 * that reacts to spending — it counts completed days only, so it has
 * nothing to say on the freeze day itself (paceDelta === 0), which is why
 * null renders nothing at all rather than a placeholder or a zero.
 *
 * Positive paceDelta means over pace; negative means under.
 */
export function formatPaceLine(paceDelta: number): string | null {
  if (Math.abs(paceDelta) < 0.005) return null
  return paceDelta > 0
    ? `you are ${formatTHB(paceDelta)} over`
    : `you are ${formatTHB(-paceDelta)} under`
}
