import {isRejectedWithValue, type Middleware} from '@reduxjs/toolkit'
import {appInsights} from './appInsights'

// TODO: add Vitest test for this middleware once a Vitest setup lands.
// The frontend currently relies on Playwright e2e — switching to Vitest
// for unit-level shape contracts is tracked in the App Insights plan
// (Task 8 was skipped because no Vitest config exists yet).

interface RtkRejectedMeta {
  arg?: {
    endpointName?: string
    originalArgs?: unknown
    type?: 'query' | 'mutation'
  }
}

/**
 * Listens for RTK Query rejections and surfaces them as App Insights
 * exception events. Only mutations are tracked — queries that fail
 * because of stale caches, route changes, or background refetches are
 * noisy and not what the maintainer needs to diagnose.
 */
export const rtkErrorTelemetry: Middleware = () => (next) => (action) => {
  if (isRejectedWithValue(action)) {
    const meta = (action.meta ?? {}) as RtkRejectedMeta
    if (meta.arg?.type === 'mutation') {
      const payload = action.payload as {status?: number | string; data?: unknown} | undefined
      appInsights.trackException({
        exception: new Error(`RTK ${meta.arg.endpointName ?? 'unknown'} rejected`),
        properties: {
          endpoint:   meta.arg.endpointName ?? 'unknown',
          args:       safeStringify(meta.arg.originalArgs),
          statusCode: String(payload?.status ?? 'unknown'),
          response:   safeStringify(payload?.data ?? null),
        },
      })
    }
  }
  return next(action)
}

function safeStringify(value: unknown): string {
  try {
    return JSON.stringify(value)
  } catch {
    return '<unserializable>'
  }
}
