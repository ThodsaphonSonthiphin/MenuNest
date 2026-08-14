import {describe, expect, it} from 'vitest'
import {captureCommitLabel, rememberedAfterExit, resolveCommitRoute} from './captureCommit'

const trip = {id: 'trip-1', name: 'เชียงใหม่ ก.ย.'}

describe('resolveCommitRoute', () => {
  it('asks the first time, because nothing is remembered yet', () => {
    expect(resolveCommitRoute(null, false)).toEqual({route: 'picker'})
  })

  it('commits straight to the remembered Trip on the next capture (R8.5)', () => {
    expect(resolveCommitRoute(trip, false)).toEqual({route: 'direct', trip})
  })

  it('the ▾ reopens the picker even with a Trip remembered', () => {
    // The defect this pins: without forcePicker a remembered Trip is a one-way
    // door and every later capture in the run lands in it.
    expect(resolveCommitRoute(trip, true)).toEqual({route: 'picker'})
  })

  it('forcePicker on a fresh run is still just the picker', () => {
    expect(resolveCommitRoute(null, true)).toEqual({route: 'picker'})
  })
})

describe('captureCommitLabel', () => {
  it('names the remembered Trip so a one-tap commit says where it goes', () => {
    expect(captureCommitLabel(trip)).toBe('เพิ่มเข้า เชียงใหม่ ก.ย.')
  })

  it('falls back to the generic label when no Trip is remembered', () => {
    expect(captureCommitLabel(null)).toBe('เพิ่มเข้าทริป')
  })
})

describe('rememberedAfterExit', () => {
  it('forgets the Trip, because the run ended with the mode', () => {
    expect(rememberedAfterExit()).toBeNull()
  })
})
