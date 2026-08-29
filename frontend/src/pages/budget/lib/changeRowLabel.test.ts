import {describe, expect, it} from 'vitest'
import {describeChange, groupByBatch} from './changeRowLabel'
import type {BudgetChangeDto} from '../../../shared/api/api'

function row(over: Partial<BudgetChangeDto> = {}): BudgetChangeDto {
  return {
    id: 'x', userId: 'u', userDisplayName: 'ทศพล', kind: 'Assign', batchId: null,
    categoryName: 'ค่ากิน', secondCategoryName: null, delta: 300, flagValue: null,
    isUndone: false, undoneByDisplayName: null, createdAt: '2026-08-20T00:00:00Z',
    canUndo: true, blockedReason: null,
    ...over,
  }
}

describe('describeChange', () => {
  it('describes an assign with the envelope and the amount', () => {
    const s = describeChange(row())
    expect(s).toContain('ค่ากิน')
    expect(s).toContain('300')
  })

  it('describes a negative assign as a reduction', () => {
    expect(describeChange(row({delta: -300}))).toContain('ลด')
  })

  it('names both envelopes in a move', () => {
    const s = describeChange(row({kind: 'Move', secondCategoryName: 'ค่าไฟ', delta: -200}))
    expect(s).toContain('ค่ากิน')
    expect(s).toContain('ค่าไฟ')
  })

  it('describes a cover from its source and its overspent envelope', () => {
    const s = describeChange(row({kind: 'Cover', secondCategoryName: 'ค่าไฟ', delta: -200}))
    expect(s).toContain('ค่าไฟ')
    expect(s).toContain('ค่ากิน')
  })

  it('describes an everyday mark by the value it was set to', () => {
    expect(describeChange(row({kind: 'EverydayMark', delta: 0, flagValue: true}))).toContain('ใช้ประจำวัน')
    expect(describeChange(row({kind: 'EverydayMark', delta: 0, flagValue: false}))).toContain('ออกจาก')
  })

  it('describes a batch as one act rather than one envelope', () => {
    expect(describeChange(row({batchId: 'b1'}))).toContain('หลายซอง')
  })
})

describe('groupByBatch', () => {
  it('collapses rows sharing a batch id into one', () => {
    const rows = [row({id: '1', batchId: 'b'}), row({id: '2', batchId: 'b'}), row({id: '3', batchId: 'b'})]
    expect(groupByBatch(rows).map(r => r.id)).toEqual(['1'])
  })

  it('keeps ungrouped rows untouched and preserves order', () => {
    const rows = [row({id: '1'}), row({id: '2', batchId: 'b'}), row({id: '3'}), row({id: '4', batchId: 'b'})]
    expect(groupByBatch(rows).map(r => r.id)).toEqual(['1', '2', '3'])
  })

  it('keeps two different batches apart', () => {
    const rows = [row({id: '1', batchId: 'a'}), row({id: '2', batchId: 'b'}), row({id: '3', batchId: 'a'})]
    expect(groupByBatch(rows).map(r => r.id)).toEqual(['1', '2'])
  })
})
