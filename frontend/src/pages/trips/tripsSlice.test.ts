// frontend/src/pages/trips/tripsSlice.test.ts
import {describe, it, expect} from 'vitest'
import reducer, {setActiveDay, setAddMode, setSelectedStop} from './tripsSlice'

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
  it('arming clears the Selected Stop — the armed map owns every tap (ADR-163)', () => {
    const selected = reducer(init, setSelectedStop('stop-1'))
    expect(reducer(selected, setAddMode(true)).selectedStopId).toBeNull()
  })
  it('disarming leaves the selection alone', () => {
    const armed = reducer(init, setAddMode(true))
    expect(reducer(armed, setAddMode(false)).selectedStopId).toBeNull()
  })
})

describe('tripsSlice Selected Stop', () => {
  it('defaults selectedStopId to null — a Trip opens with nothing selected', () => {
    expect(init.selectedStopId).toBeNull()
  })
  it('setSelectedStop stores and clears the id', () => {
    const on = reducer(init, setSelectedStop('stop-1'))
    expect(on.selectedStopId).toBe('stop-1')
    expect(reducer(on, setSelectedStop(null)).selectedStopId).toBeNull()
  })
  it('switching Day drops the selection, so the sheet never sits on a dead card', () => {
    const on = reducer(init, setSelectedStop('stop-1'))
    const moved = reducer(on, setActiveDay('day-2'))
    expect(moved.activeDayId).toBe('day-2')
    expect(moved.selectedStopId).toBeNull()
  })
})
