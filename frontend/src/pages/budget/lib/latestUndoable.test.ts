import {describe, expect, it} from 'vitest'
import {latestRedoable, latestUndoable} from './latestUndoable'
import type {BudgetChangeDto} from '../../../shared/api/api'

function row(over: Partial<BudgetChangeDto>): BudgetChangeDto {
  return {
    id: 'x', userId: 'u', userDisplayName: 'ทศพล', kind: 'Assign', batchId: null,
    categoryName: 'ค่ากิน', secondCategoryName: null, delta: 300, flagValue: null,
    isUndone: false, undoneByDisplayName: null, createdAt: '2026-08-20T00:00:00Z',
    canUndo: true, blockedReason: null,
    ...over,
  }
}

describe('latestUndoable', () => {
  it('returns null for an empty list', () => {
    expect(latestUndoable([])).toBeNull()
  })

  it('returns null when everything is already undone', () => {
    expect(latestUndoable([row({isUndone: true}), row({isUndone: true})])).toBeNull()
  })

  it('takes the FIRST match, because the list is newest-first', () => {
    const newest = row({id: 'newest'})
    const older = row({id: 'older'})
    expect(latestUndoable([newest, older])?.id).toBe('newest')
  })

  it('skips a row whose envelope was deleted and reaches the one behind it', () => {
    const dead = row({id: 'dead', canUndo: false, blockedReason: 'That envelope was deleted.'})
    const alive = row({id: 'alive'})
    expect(latestUndoable([dead, alive])?.id).toBe('alive')
  })
})

describe('latestRedoable', () => {
  it('returns the newest undone row', () => {
    const undone = row({id: 'undone', isUndone: true})
    const active = row({id: 'active'})
    expect(latestRedoable([active, undone])?.id).toBe('undone')
  })

  it('ignores an undone row whose envelope was deleted', () => {
    expect(latestRedoable([row({isUndone: true, canUndo: false})])).toBeNull()
  })

  it('returns null when nothing has been undone', () => {
    expect(latestRedoable([row({})])).toBeNull()
  })
})
