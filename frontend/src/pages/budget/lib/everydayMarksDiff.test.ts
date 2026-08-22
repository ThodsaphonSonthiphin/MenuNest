import {describe, expect, it} from 'vitest'
import {diffEverydayMarks} from './everydayMarksDiff'

// menunest-184: the whole reason EverydayMarksSheet exists is to turn N
// tick changes into ONE Budgeting event / ONE re-freeze instead of N. This
// diff is what decides that — the SPA must send exactly the envelopes whose
// tick actually differs from how the sheet opened, and nothing at all when
// nothing changed (never re-post the full list; the backend's changed-flag
// gate is a backstop, not a reason to skip this on the client).
describe('diffEverydayMarks', () => {
  const original = [
    {categoryId: 'a', isEveryday: true},
    {categoryId: 'b', isEveryday: false},
    {categoryId: 'c', isEveryday: false},
  ]

  it('returns an empty array when nothing changed', () => {
    const ticked = {a: true, b: false, c: false}
    expect(diffEverydayMarks(original, ticked)).toEqual([])
  })

  it('includes an envelope flipped from marked to unmarked', () => {
    const ticked = {a: false, b: false, c: false}
    expect(diffEverydayMarks(original, ticked)).toEqual([
      {categoryId: 'a', isEveryday: false},
    ])
  })

  it('includes an envelope flipped from unmarked to marked', () => {
    const ticked = {a: true, b: true, c: false}
    expect(diffEverydayMarks(original, ticked)).toEqual([
      {categoryId: 'b', isEveryday: true},
    ])
  })

  it('includes every envelope that changed, in original order, when several flip', () => {
    const ticked = {a: false, b: true, c: true}
    expect(diffEverydayMarks(original, ticked)).toEqual([
      {categoryId: 'a', isEveryday: false},
      {categoryId: 'b', isEveryday: true},
      {categoryId: 'c', isEveryday: true},
    ])
  })

  it('treats a categoryId missing from the ticked state as unchanged', () => {
    // Defensive: the sheet always seeds every envelope into local state on
    // open, but the diff itself must not misread "not recorded" as "flipped
    // to false" — that would silently unmark envelopes the sheet never
    // touched.
    const ticked = {a: true, b: false} // 'c' missing entirely
    expect(diffEverydayMarks(original, ticked)).toEqual([])
  })

  it('returns an empty array for an empty original list', () => {
    expect(diffEverydayMarks([], {})).toEqual([])
  })
})
