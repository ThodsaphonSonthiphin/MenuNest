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
