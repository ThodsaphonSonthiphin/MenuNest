import {describe, it, expect} from 'vitest'
import {computeReorder} from './reorder'

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
