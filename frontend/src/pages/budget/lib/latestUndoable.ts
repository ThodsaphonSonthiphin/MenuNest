import type {BudgetChangeDto} from '../../../shared/api/api'

/**
 * The history list arrives newest-first, so `find` returns the most recent
 * match in both helpers.
 *
 * A row with `canUndo === false` is skipped rather than dropped from the list:
 * menunest-197 keeps it visible on the sheet, saying why, but the rail's own
 * button must reach past it to the newest row that still works.
 */
export function latestUndoable(rows: readonly BudgetChangeDto[]): BudgetChangeDto | null {
  return rows.find(r => !r.isUndone && r.canUndo) ?? null
}

/** The newest row that has been undone and can therefore be redone. */
export function latestRedoable(rows: readonly BudgetChangeDto[]): BudgetChangeDto | null {
  return rows.find(r => r.isUndone && r.canUndo) ?? null
}
