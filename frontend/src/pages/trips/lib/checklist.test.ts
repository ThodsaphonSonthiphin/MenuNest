import {describe, expect, it} from 'vitest'
import {
  MAX_CHECKLIST_NAME,
  normalizeChecklistName,
  isValidChecklistName,
  matchLibrary,
  exactMatch,
  checklistProgress,
} from './checklist'

describe('checklist lib', () => {
  it('normalizes whitespace', () => {
    expect(normalizeChecklistName('  ร่ม  ')).toBe('ร่ม')
    expect(normalizeChecklistName('a\t b   c')).toBe('a b c')
  })

  it('validates name (non-empty, <= max)', () => {
    expect(isValidChecklistName('ร่ม')).toBe(true)
    expect(isValidChecklistName('   ')).toBe(false)
    expect(isValidChecklistName('x'.repeat(MAX_CHECKLIST_NAME))).toBe(true)
    expect(isValidChecklistName('x'.repeat(MAX_CHECKLIST_NAME + 1))).toBe(false)
  })

  it('matches library case-insensitively by substring; empty query returns all', () => {
    const items = [{name: 'Umbrella'}, {name: 'Passport'}]
    expect(matchLibrary('umb', items)).toEqual([{name: 'Umbrella'}])
    expect(matchLibrary('', items)).toHaveLength(2)
  })

  it('finds exact match case-insensitively', () => {
    const items = [{name: 'Umbrella'}]
    expect(exactMatch('umbrella', items)).toEqual({name: 'Umbrella'})
    expect(exactMatch('umb', items)).toBeNull()
  })

  it('computes checked/total progress', () => {
    expect(checklistProgress([{isChecked: true}, {isChecked: false}, {isChecked: true}])).toEqual({done: 2, total: 3})
    expect(checklistProgress([])).toEqual({done: 0, total: 0})
  })
})
