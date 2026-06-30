import {afterEach, describe, expect, it, vi} from 'vitest'
import {decodeGoogleIdToken, getGoogleToken, isGoogleTokenExpired} from './googleAuth'

// Build a base64url JWT segment (no padding), the shape Google emits.
function b64url(obj: unknown): string {
  return btoa(JSON.stringify(obj))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')
}

function makeToken(payload: Record<string, unknown>): string {
  return `${b64url({alg: 'RS256', typ: 'JWT'})}.${b64url(payload)}.sig`
}

const futureExp = () => Math.floor(Date.now() / 1000) + 3600
const pastExp = () => Math.floor(Date.now() / 1000) - 3600

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('decodeGoogleIdToken', () => {
  it('decodes an unpadded base64url payload', () => {
    const token = makeToken({sub: '1', email: 'a@b.c', name: 'Ann', exp: futureExp()})
    expect(decodeGoogleIdToken(token)?.email).toBe('a@b.c')
  })

  it('returns null for a malformed token', () => {
    expect(decodeGoogleIdToken('not-a-jwt')).toBeNull()
  })
})

describe('isGoogleTokenExpired', () => {
  it('is false for a token whose exp is comfortably in the future', () => {
    expect(isGoogleTokenExpired(makeToken({sub: '1', exp: futureExp()}))).toBe(false)
  })

  it('is true for a token whose exp is in the past', () => {
    expect(isGoogleTokenExpired(makeToken({sub: '1', exp: pastExp()}))).toBe(true)
  })

  it('is true when the token carries no exp claim', () => {
    expect(isGoogleTokenExpired(makeToken({sub: '1'}))).toBe(true)
  })

  it('is true for a malformed token', () => {
    expect(isGoogleTokenExpired('garbage')).toBe(true)
  })
})

describe('getGoogleToken', () => {
  it('returns the stored token when it is still valid', () => {
    const token = makeToken({sub: '1', exp: futureExp()})
    const removeItem = vi.fn()
    vi.stubGlobal('sessionStorage', {getItem: vi.fn(() => token), setItem: vi.fn(), removeItem})
    expect(getGoogleToken()).toBe(token)
    expect(removeItem).not.toHaveBeenCalled()
  })

  it('drops the token and returns null when it has expired', () => {
    const token = makeToken({sub: '1', exp: pastExp()})
    const removeItem = vi.fn()
    vi.stubGlobal('sessionStorage', {getItem: vi.fn(() => token), setItem: vi.fn(), removeItem})
    expect(getGoogleToken()).toBeNull()
    expect(removeItem).toHaveBeenCalledWith('google_id_token')
  })

  it('returns null when nothing is stored', () => {
    vi.stubGlobal('sessionStorage', {getItem: vi.fn(() => null), setItem: vi.fn(), removeItem: vi.fn()})
    expect(getGoogleToken()).toBeNull()
  })
})
