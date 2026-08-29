/**
 * menunest-200: Ctrl+Z and Cmd+Z both, but INERT inside an editable — the
 * browser's own undo is what a person pressing it there expects, and getting it
 * wrong moves money when they wanted their typing back — and INERT while a
 * budget dialog is open, because the dialog is showing figures the undo would
 * move underneath it with no way to know.
 */
export function classifyUndoKey(
  e: {key: string; metaKey: boolean; ctrlKey: boolean; shiftKey: boolean},
  ctx: {inEditable: boolean; dialogOpen: boolean},
): 'undo' | 'redo' | 'ignore' {
  if (ctx.inEditable || ctx.dialogOpen) return 'ignore'
  if (e.key.toLowerCase() !== 'z') return 'ignore'
  if (!e.metaKey && !e.ctrlKey) return 'ignore'
  return e.shiftKey ? 'redo' : 'undo'
}
