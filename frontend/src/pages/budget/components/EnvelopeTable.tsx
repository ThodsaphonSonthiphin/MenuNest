import type {MonthlySummaryDto} from '../../../shared/api/api'

export function EnvelopeTable({summary}: {summary: MonthlySummaryDto}) {
  return (
    <div className="budget-envelopes" style={{padding: 24, color: '#888', fontSize: 13}}>
      Envelope table — coming in next task. Summary loaded: {summary.groups.length} groups, {summary.accounts.length} accounts.
    </div>
  )
}
