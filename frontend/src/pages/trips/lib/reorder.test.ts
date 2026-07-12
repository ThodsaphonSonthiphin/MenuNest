import {describe, it, expect} from 'vitest'
import {computeReorder, reorderKeepingVisited} from './reorder'

describe('computeReorder', () => {
  const ids = ['a', 'b', 'c', 'd']

  it('moves an item down to the target slot', () => {
    expect(computeReorder(ids, 'a', 'c')).toEqual(['b', 'c', 'a', 'd'])
  })

  it('moves an item up to the target slot', () => {
    expect(computeReorder(ids, 'd', 'b')).toEqual(['a', 'd', 'b', 'c'])
  })

  it('returns null when active and over are the same', () => {
    expect(computeReorder(ids, 'b', 'b')).toBeNull()
  })

  it('returns null when the active id is not in the list', () => {
    expect(computeReorder(ids, 'x', 'c')).toBeNull()
  })

  it('returns null when the over id is not in the list', () => {
    expect(computeReorder(ids, 'a', 'x')).toBeNull()
  })

  it('preserves length and membership', () => {
    const out = computeReorder(ids, 'a', 'd')!
    expect(out).toHaveLength(4)
    expect([...out].sort()).toEqual(['a', 'b', 'c', 'd'])
  })
})

describe('reorderKeepingVisited', () => {
  it('reorders the remaining suffix while the visited prefix stays pinned', () => {
    // full a(✓) b(✓) c d e ; drag e above c → remaining [c,d,e]→[e,c,d]
    expect(reorderKeepingVisited(['a', 'b', 'c', 'd', 'e'], new Set(['a', 'b']), 'e', 'c'))
      .toEqual(['a', 'b', 'e', 'c', 'd'])
  })

  it('keeps a visited Stop pinned in the MIDDLE while remaining reorder around it', () => {
    // full a b(✓) c d ; remaining [a,c,d] ; drag d above a → [d,a,c] ; b stays at index 1
    expect(reorderKeepingVisited(['a', 'b', 'c', 'd'], new Set(['b']), 'd', 'a'))
      .toEqual(['d', 'b', 'a', 'c'])
  })

  it('returns null when active and over are the same', () => {
    expect(reorderKeepingVisited(['a', 'b', 'c'], new Set(['a']), 'b', 'b')).toBeNull()
  })

  it('returns null when a drag id is not among the remaining (e.g. it is visited)', () => {
    expect(reorderKeepingVisited(['a', 'b', 'c'], new Set(['a']), 'a', 'c')).toBeNull()
  })
})
