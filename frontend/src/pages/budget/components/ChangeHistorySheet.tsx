import {useAppSelector} from '../../../store'
import {
  useListBudgetHistoryQuery,
  useRedoBudgetChangeMutation,
  useUndoBudgetChangeMutation,
} from '../../../shared/api/api'
import {describeChange, groupByBatch} from '../lib/changeRowLabel'

/**
 * menunest-195: a SHEET over /budget on the scaffolding EverydayMarksSheet
 * already uses — not a route. Every row carries its OWN Undo and Redo, so undo
 * is not last-in-first-out, and an UNDONE ROW STAYS on the list, marked, so it
 * can be redone.
 *
 * menunest-197: a row whose envelope was deleted also stays, unpressable, with
 * its reason. The server decides that and sends `canUndo` / `blockedReason`.
 */
export function ChangeHistorySheet({onClose}: {onClose: () => void}) {
  const {year, month} = useAppSelector(s => s.budget)
  const {data = [], isLoading} = useListBudgetHistoryQuery({year, month})
  const [undoChange, undoState] = useUndoBudgetChangeMutation()
  const [redoChange, redoState] = useRedoBudgetChangeMutation()

  const rows = groupByBatch(data)
  const busy = undoState.isLoading || redoState.isLoading

  return (
    <div
      className="budget-modal-overlay"
      data-testid="bdg-history-sheet"
      onClick={(e) => { if (e.target === e.currentTarget && !busy) onClose() }}
    >
      <div className="budget-modal">
        <h3>ประวัติการแก้งบ</h3>
        <div className="subtitle">
          {/* menunest-194: the window is min(7 days, since the 1st of the month). */}
          ย้อนได้ 7 วัน และไม่ข้ามเดือน
        </div>

        {isLoading && <div className="bdg-history-empty">กำลังโหลด…</div>}

        {!isLoading && rows.length === 0 && (
          <div className="bdg-history-empty" data-testid="bdg-history-empty">
            ยังไม่มีรายการในเดือนนี้
          </div>
        )}

        <div className="bdg-history-list">
          {rows.map(r => (
            <div
              key={r.id}
              className={`bdg-history-row ${r.isUndone ? 'is-undone' : ''} ${r.canUndo ? '' : 'is-dead'}`}
              data-testid="bdg-history-row"
            >
              <div className="bdg-history-main">
                <div className="bdg-history-text">{describeChange(r)}</div>
                <div className="bdg-history-who">
                  {r.userDisplayName}
                  {r.isUndone && r.undoneByDisplayName && ` · ${r.undoneByDisplayName} ยกเลิกไว้`}
                </div>
                {!r.canUndo && r.blockedReason && (
                  <div className="bdg-history-blocked">{r.blockedReason}</div>
                )}
              </div>

              {r.isUndone ? (
                <button
                  type="button"
                  className="bdg-history-btn"
                  data-testid="bdg-history-redo"
                  disabled={!r.canUndo || busy}
                  onClick={() => void redoChange({id: r.id, year, month})}
                >ทำซ้ำ</button>
              ) : (
                <button
                  type="button"
                  className="bdg-history-btn"
                  data-testid="bdg-history-undo"
                  disabled={!r.canUndo || busy}
                  onClick={() => void undoChange({id: r.id, year, month})}
                >ยกเลิก</button>
              )}
            </div>
          ))}
        </div>

        <div className="bdg-history-actions">
          <button type="button" className="bdg-history-close" onClick={onClose} disabled={busy}>
            ปิด
          </button>
        </div>
      </div>
    </div>
  )
}
