import type { MealPlanEntryDto, StockCheckBatchDto } from '../../../shared/api/api'

interface RowStockBadgeProps {
  status: MealPlanEntryDto['status']
  /**
   * Batch stock-check result for all planned entries in the slot. Provided by
   * the parent so this component derives its display from already-fetched data
   * rather than issuing its own per-entry query (which would cause N parallel
   * requests for a slot with N planned rows).
   *
   * The batch endpoint aggregates ingredient totals across all supplied entry
   * ids, so `isSufficient` here means "the whole planned set for this slot is
   * fully covered". The ingredient-level shortfall detail is shown in the
   * footer warning banner; this badge is an at-a-glance slot-wide indicator.
   */
  stockCheck: StockCheckBatchDto | undefined
}

/**
 * Per-row stock indicator driven by the slot-wide batch stock-check result.
 *
 * Rather than calling `useGetStockCheckQuery(entryId)` per row (N requests),
 * the parent (`MealSlotDetail` via `useMealSlotDetail`) issues a single batch
 * query covering all planned entries and passes the result down here. This
 * keeps per-row display cheap — no additional network traffic per row.
 */
export function RowStockBadge({ status, stockCheck }: RowStockBadgeProps) {
  if (status !== 'Planned') return <span style={{ color: 'var(--color-text-muted)' }}>—</span>
  if (!stockCheck) return <span style={{ color: 'var(--color-text-muted)' }}>…</span>
  return stockCheck.isSufficient ? (
    <span style={{ color: 'green' }}>✅ พอ</span>
  ) : (
    <span style={{ color: 'var(--color-danger)' }}>⚠️ ขาด {stockCheck.missingCount} อย่าง</span>
  )
}
