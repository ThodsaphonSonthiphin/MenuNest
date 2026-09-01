import {describe, expect, it} from 'vitest'
import {latestRedoable, latestUndoable} from './latestUndoable'
import type {BudgetChangeDto} from '../../../shared/api/api'

function row(over: Partial<BudgetChangeDto>): BudgetChangeDto {
  return {
    id: 'x', userId: 'u', userDisplayName: 'ทศพล', kind: 'Assign', batchId: null,
    categoryName: 'ค่ากิน', secondCategoryName: null, delta: 300, flagValue: null,
    isUndone: false, undoneByDisplayName: null, createdAt: '2026-08-20T00:00:00Z',
    canUndo: true, canRedo: false, isDead: false, blockedReason: null,
    ...over,
  }
}

/**
 * An undone row as the server really sends it (menunest-216): the undo button is
 * gone, so `canUndo` is false for everyone and `canRedo` carries the permission.
 */
function undoneRow(over: Partial<BudgetChangeDto> = {}): BudgetChangeDto {
  return row({isUndone: true, canUndo: false, canRedo: true, ...over})
}

describe('latestUndoable', () => {
  it('returns null for an empty list', () => {
    expect(latestUndoable([])).toBeNull()
  })

  it('returns null when everything is already undone', () => {
    expect(latestUndoable([undoneRow(), undoneRow()])).toBeNull()
  })

  it('takes the FIRST match, because the list is newest-first', () => {
    const newest = row({id: 'newest'})
    const older = row({id: 'older'})
    expect(latestUndoable([newest, older])?.id).toBe('newest')
  })

  it('skips a row whose envelope was deleted and reaches the one behind it', () => {
    const dead = row({
      id: 'dead', canUndo: false, isDead: true,
      blockedReason: 'That envelope was deleted.',
    })
    const alive = row({id: 'alive'})
    expect(latestUndoable([dead, alive])?.id).toBe('alive')
  })

  it('reaches PAST another member\'s newer change to the caller\'s own older one', () => {
    // menunest-216 §6 accepts this deliberately: the rail always undoes the
    // newest thing YOU may undo. It was weighed against stopping at the newest
    // row and against announcing the skip, and both were rejected. A change that
    // makes this return null is undoing a decision, not fixing a bug.
    const theirs = row({
      id: 'theirs', userId: 'malee', userDisplayName: 'มาลี', canUndo: false,
      blockedReason: 'Only the family head can undo someone else\'s change.',
    })
    const mine = row({id: 'mine'})
    expect(latestUndoable([theirs, mine])?.id).toBe('mine')
  })
})

describe('latestRedoable', () => {
  it('returns the newest undone row', () => {
    const undone = undoneRow({id: 'undone'})
    const active = row({id: 'active'})
    expect(latestRedoable([active, undone])?.id).toBe('undone')
  })

  it('ignores an undone row whose envelope was deleted', () => {
    expect(latestRedoable([undoneRow({canRedo: false, isDead: true})])).toBeNull()
  })

  it('ignores a row the family head undid, even though the caller authored it', () => {
    // menunest-216 §2: redo belongs to whoever UNDID the row. Reading canUndo
    // here would arm the rail on a row the server then refuses.
    const undoneByHead = undoneRow({
      id: 'undone-by-head', canRedo: false, undoneByDisplayName: 'ทศพล',
      blockedReason: 'Only whoever undid this, or the family head, can redo it.',
    })
    expect(latestRedoable([undoneByHead])).toBeNull()
  })

  it('returns null when nothing has been undone', () => {
    expect(latestRedoable([row({})])).toBeNull()
  })
})
