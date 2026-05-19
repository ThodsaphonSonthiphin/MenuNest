import { Buffer } from 'node:buffer'
import type { Page } from '@playwright/test'

export interface GoogleTokenPayload {
  sub?: string
  name?: string
  email?: string
}

export const buildGoogleToken = (payload: GoogleTokenPayload = {}): string => {
  const encoded = Buffer.from(
    JSON.stringify({
      sub: payload.sub ?? 'user-1',
      name: payload.name ?? 'Test User',
      email: payload.email ?? 'test@menunest.app',
    }),
  ).toString('base64')
  return `header.${encoded}.signature`
}

export const applyGoogleAuth = async (page: Page, payload?: GoogleTokenPayload) => {
  const token = buildGoogleToken(payload)
  await page.addInitScript((value) => {
    sessionStorage.setItem('google_id_token', value)
  }, token)
  return token
}
