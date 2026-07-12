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
