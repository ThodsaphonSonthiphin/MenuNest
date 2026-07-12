// Pure reorder logic for the itinerary drag-and-drop (ADR-043/046). Kept free of
// @dnd-kit so the drop decision is unit-testable without simulating gestures.

/**
 * Move `activeId` into `overId`'s slot within `ids`. Returns the new order, or
 * `null` when nothing would change (same id) or either id is not present.
 */
export function computeReorder(ids: string[], activeId: string, overId: string): string[] | null {
  if (activeId === overId) return null
  const from = ids.indexOf(activeId)
  const to = ids.indexOf(overId)
  if (from < 0 || to < 0) return null
  const next = ids.slice()
  next.splice(to, 0, next.splice(from, 1)[0])
  return next
}

/**
 * Reorder only the not-visited Stops among themselves; visited ids keep their original
 * index (ADR-048). Returns the FULL-day ordered ids to send to reorderStops, or `null`
 * when nothing changes (delegates the change/lookup rules to computeReorder).
 */
export function reorderKeepingVisited(
  fullIds: string[],
  visitedIds: ReadonlySet<string>,
  activeId: string,
  overId: string,
): string[] | null {
  const remainingIds = fullIds.filter((id) => !visitedIds.has(id))
  const nextRemaining = computeReorder(remainingIds, activeId, overId)
  if (!nextRemaining) return null
  let ri = 0
  return fullIds.map((id) => (visitedIds.has(id) ? id : nextRemaining[ri++]))
}
