import type { Page, Request, Route } from '@playwright/test'

export interface CapturedRequest {
  method: string
  url: string
  pathname: string
  body: unknown
  headers: Record<string, string>
}

export interface RequestCapture {
  push: (req: CapturedRequest) => void
  all: () => CapturedRequest[]
  waitFor: (method: string, pathMatcher: string | RegExp, timeoutMs?: number) => Promise<CapturedRequest>
  clear: () => void
}

export const createCapture = (): RequestCapture => {
  const items: CapturedRequest[] = []
  const listeners: Array<() => void> = []
  return {
    push: (req) => {
      items.push(req)
      listeners.splice(0).forEach((fn) => fn())
    },
    all: () => [...items],
    clear: () => {
      items.length = 0
    },
    waitFor: (method, pathMatcher, timeoutMs = 5_000) =>
      new Promise<CapturedRequest>((resolve, reject) => {
        const deadline = Date.now() + timeoutMs
        const test = () =>
          items.find(
            (r) =>
              r.method === method.toUpperCase() &&
              (typeof pathMatcher === 'string'
                ? r.pathname === pathMatcher
                : pathMatcher.test(r.pathname)),
          )
        const initial = test()
        if (initial) return resolve(initial)
        const tick = () => {
          const hit = test()
          if (hit) return resolve(hit)
          if (Date.now() > deadline) {
            return reject(new Error(`Timed out waiting for ${method} ${pathMatcher}`))
          }
          listeners.push(tick)
        }
        listeners.push(tick)
      }),
  }
}

/**
 * Shared `/api/me` fixture. `budgetRoutes.ts`, `chatRoutes.ts` and
 * `tripRoutes.ts` each register a route for `/api/me` (`.apply()`), so if a
 * spec ever composes two of those `.apply()`s on the same page, be aware
 * Playwright matches routes in REVERSE registration order — the LAST
 * `.apply()` called wins `/api/me`, not the first. No spec does this today.
 */
export const meResponse = {
  userId: 'user-1',
  email: 'test@menunest.app',
  displayName: 'ทศพล',
  familyId: 'family-1',
  familyName: 'ครอบครัวทดสอบ',
  familyInviteCode: 'TEST01',
  authProvider: 'Google',
  homePath: null,
  uvWarnThreshold: null,
  feelsLikeWarnThreshold: null,
  activeTargetRule: null,
}

export const recordRequest = async (
  route: Route,
  request: Request,
  capture: RequestCapture,
): Promise<void> => {
  const url = new URL(request.url())
  let body: unknown = null
  try {
    body = request.postDataJSON()
  } catch {
    body = request.postData()
  }
  capture.push({
    method: request.method(),
    url: request.url(),
    pathname: url.pathname,
    body,
    headers: request.headers(),
  })
}
