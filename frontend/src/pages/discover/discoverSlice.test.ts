import {describe, it, expect} from 'vitest'
import reducer, {setAnchor, setScope, setCategoryFilter, toggleSignal, setSelectedKey, initialState} from './discoverSlice'

describe('discoverSlice', () => {
  it('defaults: openNow/season/hideVisited on, bestTime off, category all', () => {
    expect(initialState.toggles).toEqual({openNow: true, season: true, bestTime: false, hideVisited: true})
    expect(initialState.categoryFilter).toBe('all')
    expect(initialState.anchor).toBeNull()
  })
  it('setAnchor stores coordinates', () => {
    const s = reducer(initialState, setAnchor({lat: 13.75, lng: 100.5}))
    expect(s.anchor).toEqual({lat: 13.75, lng: 100.5})
  })
  it('toggleSignal flips one toggle without touching the others', () => {
    const s = reducer(initialState, toggleSignal('bestTime'))
    expect(s.toggles.bestTime).toBe(true)
    expect(s.toggles.openNow).toBe(true)
  })
  it('setScope + setCategoryFilter + setSelectedKey update their fields', () => {
    let s = reducer(initialState, setScope({north: 1, south: 0, east: 1, west: 0}))
    s = reducer(s, setCategoryFilter('Eat'))
    s = reducer(s, setSelectedKey('gp-1'))
    expect(s.scope).toEqual({north: 1, south: 0, east: 1, west: 0})
    expect(s.categoryFilter).toBe('Eat')
    expect(s.selectedKey).toBe('gp-1')
  })
})
