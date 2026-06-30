import {SeverityLevel} from '@microsoft/applicationinsights-web'
import {appInsights} from './appInsights'

// gm_authFailure is a global the Google Maps JS API invokes itself.
declare global {
  interface Window {
    gm_authFailure?: () => void
  }
}

/**
 * Google Maps reports failures in three ways, NONE of which the
 * Application Insights browser SDK captures by default (autocapture only
 * hooks uncaught `window.onerror` exceptions and fetch/XHR dependencies):
 *
 *   1. `window.gm_authFailure()` — a global the Maps JS API calls on ANY
 *      auth failure (invalid/expired key, RefererNotAllowed, billing off).
 *      This renders the "This page can't load Google Maps correctly"
 *      overlay. No-arg, so it tells us auth failed but not why.
 *   2. `console.error("Google Maps JavaScript API error: <CODE> ...")` —
 *      the specific machine-readable code (e.g. RefererNotAllowedMapError),
 *      plus warnings like "initialized without a valid Map ID". These are
 *      console.* calls, not thrown exceptions, so autocapture misses them.
 *   3. `<APIProvider onError>` — loader-level failures (script 404, network).
 *
 * `wireGoogleMapsTelemetry()` forwards (1) and (2); (3) is wired at the
 * <APIProvider> call site via `trackGoogleMapsError`. All paths are
 * best-effort and never throw — telemetry must not break the map or the
 * console.
 */

/**
 * True if a console.error payload looks like a Google Maps diagnostic.
 * Pure + exported so the matching logic is unit-testable without touching
 * the global console.
 */
export function isGoogleMapsErrorText(text: string): boolean {
  return /Google Maps JavaScript API|valid Map ID|gm_authFailure|RefererNotAllowed|BillingNotEnabled|ApiNotActivated|InvalidKey|ExpiredKey|\wMapError/i.test(
    text,
  )
}

function safe(v: unknown): string {
  try {
    return typeof v === 'string' ? v : String(v)
  } catch {
    return '<unstringifiable>'
  }
}

let wired = false

/**
 * Idempotent. Wires gm_authFailure + a narrow console.error interceptor to
 * App Insights. No-op when telemetry is disabled (no connection string) so
 * local dev never has its console patched.
 */
export function wireGoogleMapsTelemetry(): void {
  if (wired || typeof window === 'undefined') return
  if (!import.meta.env.VITE_APPINSIGHTS_CONNECTION_STRING) return
  wired = true

  // (1) Auth-failure overlay — the headline production failure mode.
  window.gm_authFailure = () => {
    appInsights.trackException({
      exception: new Error('Google Maps auth failure (gm_authFailure)'),
      severityLevel: SeverityLevel.Error,
      properties: {
        source: 'gm_authFailure',
        hint: 'invalid/expired key, referrer not allowed, or billing disabled',
        url: window.location.href,
      },
    })
  }

  // (2) Maps-prefixed console.error messages carry the specific error code.
  const original = console.error.bind(console)
  console.error = (...args: unknown[]) => {
    try {
      const text = args.map(safe).join(' ')
      if (isGoogleMapsErrorText(text)) {
        appInsights.trackException({
          exception: new Error('Google Maps error: ' + text.slice(0, 300)),
          severityLevel: SeverityLevel.Error,
          properties: {source: 'console.error', message: text.slice(0, 2000), url: window.location.href},
        })
      }
    } catch {
      /* telemetry must never break the console */
    }
    original(...args)
  }
}

/**
 * For `<APIProvider onError>` (loader-level failures). Safe to pass
 * directly: `<APIProvider onError={trackGoogleMapsError}>`.
 */
export function trackGoogleMapsError(error: unknown): void {
  appInsights.trackException({
    exception: error instanceof Error ? error : new Error('Google Maps APIProvider error: ' + safe(error)),
    severityLevel: SeverityLevel.Error,
    properties: {source: 'APIProvider.onError'},
  })
}
