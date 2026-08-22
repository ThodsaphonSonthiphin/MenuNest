import {useState} from 'react'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {useSetEverydayMarksMutation, type EnvelopeGroupDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {getViewerTimeZone} from '../../../shared/utils/timeZone'
import {diffEverydayMarks, type EverydayMarkDiffEntry} from '../lib/everydayMarksDiff'

/**
 * Bulk everyday-envelope picker (menunest-181/184). Lists **every** envelope
 * across **every** group — no group filter, because the mark lives on the
 * Envelope and is group-independent (menunest-181): filtering by group, or
 * by the page's own overspent/underfunded/snoozed filter, would hide
 * envelopes the user needs to mark.
 *
 * Ticks are local state only while the sheet is open. **Closing the sheet
 * is the commit point** — an overlay tap and the Done button both count as
 * "close" — sending at most one bulk request for the envelopes whose tick
 * actually differs from how the sheet opened. That single request is what
 * turns marking six envelopes into ONE Budgeting event / ONE re-freeze of
 * the Daily allowance, instead of six visible jumps of the headline
 * (menunest-184) — do not add a per-row save.
 *
 * The diff is computed by `diffEverydayMarks` (unit-tested — the one part
 * of this component vitest can actually verify, per this environment having
 * no jsdom/component harness). Closing with nothing changed sends nothing:
 * the diff comes back empty and the mutation is never called.
 *
 * Unlike the sibling dialogs (MoveMoneyDialog, CoverOverspendingDialog,
 * QuickAssignDialog), an overlay click here does NOT discard — there is no
 * "cancel" state to discard, since nothing is saved until close.
 */
export function EverydayMarksSheet({groups, onClose}: {
  groups: EnvelopeGroupDto[]
  onClose: () => void
}) {
  const [setMarks, {isLoading}] = useSetEverydayMarksMutation()
  const [err, setErr] = useState<string | null>(null)

  // Both snapshotted once, via a lazy initializer, so a background summary
  // refetch while the sheet is open (e.g. another tab/device changes a mark)
  // can never reset in-progress ticks or skew the close-time diff against a
  // moving target — the diff is always "what changed from what this sheet
  // showed when it opened".
  const [original] = useState<EverydayMarkDiffEntry[]>(() =>
    groups.flatMap(g => g.categories.map(c => ({categoryId: c.categoryId, isEveryday: c.isEveryday}))))
  const [ticked, setTicked] = useState<Record<string, boolean>>(() =>
    Object.fromEntries(original.map(o => [o.categoryId, o.isEveryday])))

  const toggle = (categoryId: string) =>
    setTicked(prev => ({...prev, [categoryId]: !prev[categoryId]}))

  const commitAndClose = async () => {
    const diff = diffEverydayMarks(original, ticked)
    if (diff.length === 0) {
      onClose()
      return
    }
    setErr(null)
    try {
      await setMarks({marks: diff, timeZoneId: getViewerTimeZone()}).unwrap()
      onClose()
    } catch (e) {
      setErr(getErrorMessage(e))
    }
  }

  const hasEnvelopes = groups.some(g => g.categories.length > 0)

  return (
    <div
      className="budget-modal-overlay"
      data-testid="bdg-everyday-sheet"
      onClick={(e) => { if (e.target === e.currentTarget && !isLoading) commitAndClose() }}
    >
      <div className="budget-modal">
        <h3>Everyday envelopes</h3>
        <div className="subtitle">
          Mark which envelopes are your everyday spending — today's allowance splits across these.
        </div>

        {hasEnvelopes ? (
          <div className="bdg-everyday-list">
            {groups.map(g => g.categories.length > 0 && (
              <div key={g.groupId} className="bdg-everyday-group">
                <div className="bdg-everyday-group-header">{g.name}</div>
                {g.categories.map(c => (
                  <label key={c.categoryId} className="bdg-everyday-row" data-testid="bdg-everyday-row">
                    <input
                      type="checkbox"
                      checked={ticked[c.categoryId] ?? false}
                      onChange={() => toggle(c.categoryId)}
                      disabled={isLoading}
                    />
                    <span className="bdg-everyday-emoji">{c.emoji ?? '•'}</span>
                    <span className="bdg-everyday-name">{c.name}</span>
                  </label>
                ))}
              </div>
            ))}
          </div>
        ) : (
          <div className="bdg-qa-empty">
            No envelopes yet. Add an envelope first, then come back to mark it as everyday spending.
          </div>
        )}

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button
            type="button"
            variant={Variant.Filled}
            color={Color.Primary}
            onClick={commitAndClose}
            disabled={isLoading}
          >
            {isLoading ? '…' : 'Done'}
          </Button>
        </div>
      </div>
    </div>
  )
}
