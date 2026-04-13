import { useGetStockCheckQuery } from '../../../shared/api/api'
import type { MealPlanEntryDto } from '../../../shared/api/api'

interface RowStockBadgeProps {
  entryId: string
  status: MealPlanEntryDto['status']
}

/**
 * Per-row stock badge — reuses the single-entry stock check so each
 * row tells the user "this one alone is short". The selected-set
 * banner above the footer reports the aggregate.
 *
 * TODO: consolidate into the batch endpoint if slot sizes regularly exceed 4.
 */
export function RowStockBadge({ entryId, status }: RowStockBadgeProps) {
  const { data } = useGetStockCheckQuery(entryId, { skip: status !== 'Planned' })
  if (status !== 'Planned') return <span style={{ color: 'var(--color-text-muted)' }}>—</span>
  if (!data) return <span style={{ color: 'var(--color-text-muted)' }}>…</span>
  return data.isSufficient ? (
    <span style={{ color: 'green' }}>✅ พอ</span>
  ) : (
    <span style={{ color: 'var(--color-danger)' }}>⚠️ ขาด {data.missingCount} อย่าง</span>
  )
}
