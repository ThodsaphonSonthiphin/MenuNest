// frontend/src/pages/trips/tripsSlice.test.ts
import {describe, it, expect} from 'vitest'
import reducer, {setAddMode, setItineraryMapCollapsed} from './tripsSlice'

const init = reducer(undefined, {type: '@@INIT'})

describe('tripsSlice add-mode', () => {
  it('defaults addMode to false', () => {
    expect(init.addMode).toBe(false)
  })
  it('setAddMode toggles the flag', () => {
    const on = reducer(init, setAddMode(true))
    expect(on.addMode).toBe(true)
    const off = reducer(on, setAddMode(false))
    expect(off.addMode).toBe(false)
  })
})

describe('tripsSlice itinerary map band', () => {
  it('defaults itineraryMapCollapsed to false (map expanded on open — fixes #8)', () => {
    expect(init.itineraryMapCollapsed).toBe(false)
  })
  it('setItineraryMapCollapsed toggles the flag', () => {
    const collapsed = reducer(init, setItineraryMapCollapsed(true))
    expect(collapsed.itineraryMapCollapsed).toBe(true)
    const expanded = reducer(collapsed, setItineraryMapCollapsed(false))
    expect(expanded.itineraryMapCollapsed).toBe(false)
  })
})
