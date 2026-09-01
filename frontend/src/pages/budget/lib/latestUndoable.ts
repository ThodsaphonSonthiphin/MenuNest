import type {BudgetChangeDto} from '../../../shared/api/api'

/**
 * The history list arrives newest-first, so `find` returns the most recent
 * match in both helpers.
 *
 * A row the caller cannot act on is skipped rather than dropped from the list:
 * menunest-197 keeps it visible on the sheet, saying why, but the rail's own
 * button must reach past it to the newest row that still works.
 *
 * menunest-216 widened what "cannot act on" means, and ACCEPTED the consequence
 * rather than inheriting it: `canUndo` now also carries ownership, so for an
 * ordinary member the rail steps over a colleague's newer change and arms on
 * their own older one, silently. That is the chosen behaviour — the rail always
 * undoes the newest thing YOU may undo. Do not "fix" it into stopping at the
 * newest row; that was weighed and rejected, because the rail would go dark
 * whenever anyone else in the family was active.
 */
export function latestUndoable(rows: readonly BudgetChangeDto[]): BudgetChangeDto | null {
  return rows.find(r => !r.isUndone && r.canUndo) ?? null
}

/**
 * The newest row that has been undone and that the caller may redo. It reads
 * `canRedo`, NOT `canUndo`: menunest-216 governs redo by who undid the row, so
 * a change the family head undid is not redoable by its author.
 */
export function latestRedoable(rows: readonly BudgetChangeDto[]): BudgetChangeDto | null {
  return rows.find(r => r.isUndone && r.canRedo) ?? null
}
