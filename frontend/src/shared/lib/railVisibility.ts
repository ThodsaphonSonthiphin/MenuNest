export interface RailScrollState {
  hidden: boolean
  lastY: number
}

export const initialRailScrollState: RailScrollState = {hidden: false, lastY: 0}

/** Ignore anything smaller than a real flick. */
const JITTER = 8
/** Never hide at the very top of the page — there is nothing to uncover yet. */
const FLOOR = 40

/**
 * menunest-192's two guards live here, which is why this is a pure function
 * rather than logic buried in a scroll handler: the rail never hides while the
 * dial is open, and a small wobble never moves it.
 *
 * The idle-return timer belongs to the component instead — it is a timeout,
 * not a decision.
 */
export function decideRailVisibility(
  prev: RailScrollState,
  next: {scrollTop: number; isOpen: boolean},
): RailScrollState {
  if (next.isOpen) return {hidden: false, lastY: next.scrollTop}

  const dy = next.scrollTop - prev.lastY
  if (Math.abs(dy) <= JITTER) return prev

  const hidden = dy > 0 && next.scrollTop > FLOOR
  return {hidden, lastY: next.scrollTop}
}
