// frontend/src/pages/discover/lib/captureCommit.ts
// Where a finished Discover capture commits, and what its primary action is called.
//
// Extracted from DiscoverPage because this is the part of R8.5 that has a wrong
// answer: a remembered Trip that cannot be changed silently misfiles every later
// capture in the run. Component state is unreachable from vitest's node
// environment (CLAUDE.md), so the rule lives here where a test can pin it.
export interface RememberedTrip {
  id: string
  name: string
}

export type CommitRoute =
  /** Commit straight to the Trip this run already chose — one tap (R8.5). */
  | {route: 'direct'; trip: RememberedTrip}
  /** Ask. Either nothing is remembered yet, or the user asked to change Trip. */
  | {route: 'picker'}

/**
 * R8.5: "arming remembers the Trip chosen at the previous save, so a run of captures
 * picks a Trip once … the `▾` opens the trip picker."
 *
 * `forcePicker` is that `▾`. Without it a remembered Trip is a one-way door: every
 * later capture lands in the first Trip picked, with no way back to the list.
 */
export function resolveCommitRoute(remembered: RememberedTrip | null, forcePicker: boolean): CommitRoute {
  if (forcePicker || remembered === null) return {route: 'picker'}
  return {route: 'direct', trip: remembered}
}

/**
 * The primary action's label. Naming the Trip is what makes the one-tap commit
 * honest — an unlabelled "เพิ่มเข้าทริป" that silently reuses a Trip chosen several
 * captures ago would not tell the user where the place is about to go.
 */
export function captureCommitLabel(remembered: RememberedTrip | null): string {
  return remembered ? `เพิ่มเข้า ${remembered.name}` : 'เพิ่มเข้าทริป'
}

/**
 * A run ends when capture mode is exited, so the remembered Trip must not outlive it.
 * Kept as a named function rather than an inline `null` so the rule is greppable from
 * the test that pins it.
 */
export function rememberedAfterExit(): RememberedTrip | null {
  return null
}
