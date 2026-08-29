import type {BudgetChangeDto} from '../../../shared/api/api'
import {formatTHB} from './formatTHB'

/**
 * One human line per history row. Written from the user's side of the screen:
 * envelope names and money, never entity or kind names.
 */
export function describeChange(row: BudgetChangeDto): string {
  const amount = formatTHB(Math.abs(row.delta))

  if (row.batchId) return 'แจกเงินเข้าหลายซอง'

  switch (row.kind) {
    case 'Assign':
      return row.delta >= 0
        ? `ใส่ ${amount} เข้า ${row.categoryName}`
        : `ลด ${row.categoryName} ลง ${amount}`
    case 'Move':
      return `ย้าย ${amount} จาก ${row.categoryName} ไป ${row.secondCategoryName ?? '—'}`
    case 'Cover':
      return `ปิดยอดเกินของ ${row.secondCategoryName ?? '—'} ด้วย ${amount} จาก ${row.categoryName}`
    case 'EverydayMark':
      return row.flagValue
        ? `ทำเครื่องหมาย ${row.categoryName} เป็นซองใช้ประจำวัน`
        : `เอาเครื่องหมายใช้ประจำวันออกจาก ${row.categoryName}`
  }
}

/**
 * menunest-196: one press of a quick-assign chip writes N rows sharing a
 * BatchId, and the sheet must show that press as ONE entry. The first row of
 * each batch stands for the whole batch; ungrouped rows pass through.
 *
 * The input is newest-first and the output preserves that order.
 */
export function groupByBatch(rows: readonly BudgetChangeDto[]): BudgetChangeDto[] {
  const seen = new Set<string>()
  const out: BudgetChangeDto[] = []
  for (const r of rows) {
    if (r.batchId) {
      if (seen.has(r.batchId)) continue
      seen.add(r.batchId)
    }
    out.push(r)
  }
  return out
}
