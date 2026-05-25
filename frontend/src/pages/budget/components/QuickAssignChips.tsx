import {useState} from 'react'
import type {MonthlySummaryDto} from '../../../shared/api/api'
import {QuickAssignDialog, type QuickAssignMode} from './QuickAssignDialog'

/**
 * Two-chip surface that appears under the RTA hero ONLY when
 * readyToAssign > 0. Each chip opens a preview dialog that shows
 * how the leftover would be distributed before the user commits.
 */
export function QuickAssignChips({summary}: {summary: MonthlySummaryDto}) {
  const [mode, setMode] = useState<QuickAssignMode | null>(null)
  if (summary.readyToAssign <= 0) return null

  return (
    <>
      <div className="bdg-qa-chips" data-testid="bdg-qa-chips">
        <button
          type="button"
          className="bdg-qa-chip"
          onClick={() => setMode('targets')}
          data-testid="bdg-qa-chip-targets"
        >+ Fill targets first</button>
        <button
          type="button"
          className="bdg-qa-chip"
          onClick={() => setMode('equally')}
          data-testid="bdg-qa-chip-equally"
        >+ Equally to envelopes</button>
      </div>
      {mode && (
        <QuickAssignDialog
          summary={summary}
          mode={mode}
          onClose={() => setMode(null)}
        />
      )}
    </>
  )
}
