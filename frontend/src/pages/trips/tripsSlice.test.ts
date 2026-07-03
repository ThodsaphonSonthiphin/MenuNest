// frontend/src/pages/trips/tripsSlice.test.ts
import {describe, it, expect} from 'vitest'
import reducer, {setAddMode} from './tripsSlice'

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
