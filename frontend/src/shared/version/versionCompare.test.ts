import { describe, it, expect } from 'vitest'
import { commitOf, inSync } from './versionCompare'

describe('commitOf', () => {
  it('returns the part after +', () => expect(commitOf('0.1.0+a1b2c3d')).toBe('a1b2c3d'))
  it('passes through a bare sha', () => expect(commitOf('a1b2c3d')).toBe('a1b2c3d'))
})

describe('inSync', () => {
  it('true when commits equal', () => expect(inSync('a1b2c3d', 'a1b2c3d')).toBe(true))
  it('false when commits differ', () => expect(inSync('a1b2c3d', '9f3e0c1')).toBe(false))
  it('false when api commit missing', () => expect(inSync('a1b2c3d', undefined)).toBe(false))
  it('false when app commit empty', () => expect(inSync('', 'a1b2c3d')).toBe(false))
})
