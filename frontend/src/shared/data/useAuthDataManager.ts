import { useState, useEffect, useMemo } from 'react'
import { DataManager, WebApiAdaptor } from '@syncfusion/react-data'
import type { DataOptions } from '@syncfusion/react-data'
import { useMsal } from '@azure/msal-react'
import { acquireAccessToken } from '../api/api'
import { getAppSession } from '../auth/appSession'

// `||` not `??`: an unset CI secret renders as '' (not undefined); '' ?? fallback
// keeps the empty string → requests hit relative paths instead of the API.
const API_BASE = import.meta.env.VITE_API_BASE_URL || 'https://localhost:5001'

// ---------------------------------------------------------------------------
// Auth adaptor – exported so pages can extend it further
// ---------------------------------------------------------------------------

/**
 * WebApiAdaptor subclass that:
 * 1. Injects `Authorization: Bearer <token>` on every request.
 * 2. Sends PUT to `url/{id}` (REST convention) instead of plain `url`.
 * 3. Optionally rewrites the GET URL and transforms the GET response.
 *
 * The first argument is a **getter**, not a token string — public API, changed
 * deliberately. A captured string is fixed for the lifetime of the adaptor, and
 * the adaptor outlives the 1-hour app-session access token: a page left open
 * would send the same expired bearer forever. `beforeSend` is synchronous and
 * cannot await a refresh, so the getter must return whatever token is valid
 * *now*; pass one that reads the stored session (see `useAuthDataManager`) and
 * the adaptor automatically picks up any rotation another caller has performed.
 */
export class AuthAdaptor extends WebApiAdaptor {
  private _getToken: () => string
  private _readUrl: string | undefined
  private _transformResponse: ((data: unknown) => unknown) | undefined

  constructor(
    getToken: () => string,
    opts?: {
      /** If provided, GET requests hit this URL instead of the base URL. */
      readUrl?: string
      /** Transform the raw GET response before the Grid receives it. */
      transformResponse?: (data: unknown) => unknown
    },
  ) {
    super()
    this._getToken = getToken
    this._readUrl = opts?.readUrl
    this._transformResponse = opts?.transformResponse
  }

  override beforeSend(dm: DataManager, request: Request, settings?: unknown): void {
    super.beforeSend(dm, request, settings as never)
    // Read per request, never once at construction — that is the whole point
    // of taking a getter.
    request.headers.set('Authorization', `Bearer ${this._getToken()}`)
  }

  override processQuery(
    dm: DataManager,
    query: unknown,
    hierarchyFilters?: object[],
  ): object {
    // Temporarily swap the URL for the read request, then restore it.
    if (this._readUrl) {
      const original = dm.dataSource.url
      dm.dataSource.url = this._readUrl
      const result = super.processQuery(dm, query as never, hierarchyFilters)
      dm.dataSource.url = original
      return result
    }
    return super.processQuery(dm, query as never, hierarchyFilters)
  }

  override processResponse(
    data: object,
    ds?: object,
    query?: object,
    xhr?: Request,
    request?: object,
    changes?: object,
  ): object {
    // Our REST API returns plain arrays for list endpoints, not the
    // { Items: [...], Count: N } format that WebApiAdaptor expects.
    // Detect and wrap so the Grid receives the correct DataResult shape.
    let normalized = data
    if (this._transformResponse) {
      normalized = this._transformResponse(data) as object
    }
    if (Array.isArray(normalized)) {
      normalized = { result: normalized, count: normalized.length }
    }
    return super.processResponse(
      normalized as never, ds as never, query as never, xhr, request as never, changes as never,
    )
  }

  override update(
    dm: DataManager,
    keyField: string,
    value: Record<string, unknown>,
    _tableName?: string, // eslint-disable-line @typescript-eslint/no-unused-vars
  ): object {
    const id = value[keyField]
    return {
      type: 'PUT',
      url: `${dm.dataSource.url}/${id}`,
      data: JSON.stringify(value),
    }
  }
}

// ---------------------------------------------------------------------------
// React hook
// ---------------------------------------------------------------------------

export interface UseAuthDataManagerOptions {
  /**
   * Relative API path used for CUD operations, e.g. '/api/ingredients'.
   * Also used for GET unless `readUrl` is specified.
   */
  url: string
  /** Primary key field name. Defaults to 'id'. */
  key?: string
  /**
   * Separate relative API path for GET requests.
   * Useful when the list data lives at a different endpoint than CUD
   * (e.g. items are embedded in a parent detail response).
   */
  readUrl?: string
  /**
   * Transform the raw GET response before the Grid receives it.
   * Called after the adaptor's standard response processing.
   */
  transformResponse?: (data: unknown) => unknown
}

/**
 * Returns a `DataManager` wired to a REST API endpoint with MSAL auth.
 *
 * Returns `null` until a first token has been acquired — that acquisition is
 * what establishes (or refreshes) the durable app session, so it is the gate,
 * not the source of the header.
 *
 * The Bearer header itself is read from the stored app session on **every**
 * request, not captured when the DataManager is built. That is the actual
 * guarantee: the Grid sends whatever access token is currently stored,
 * including one an RTK Query call on the same page has just rotated in. It is
 * NOT a guarantee that the token is unexpired — `beforeSend` is synchronous
 * and cannot await a refresh, and this hook does not re-run on its own. If no
 * session is stored the header goes out empty and the API answers 401.
 *
 * @example
 * ```tsx
 * // Simple — one URL for everything
 * const dm = useAuthDataManager({ url: '/api/ingredients' })
 *
 * // Advanced — separate read URL + response transform
 * const dm = useAuthDataManager({
 *   url: `/api/shopping-lists/${id}/items`,           // CUD
 *   readUrl: `/api/shopping-lists/${id}`,             // GET
 *   transformResponse: (data) => (data as Detail).items.filter(i => !i.isBought),
 * })
 * ```
 */
export function useAuthDataManager(
  opts: UseAuthDataManagerOptions,
): DataManager | null {
  const { url, key = 'id', readUrl, transformResponse } = opts
  const { instance, accounts } = useMsal()
  const [token, setToken] = useState<string>('')

  useEffect(() => {
    let cancelled = false
    acquireAccessToken()
      .then((t) => {
        if (!cancelled && t) setToken(t)
      })
      .catch(() => {
        /* token failure is handled by the shared acquire path */
      })
    return () => {
      cancelled = true
    }
  }, [instance, accounts])

  return useMemo(() => {
    if (!token) return null

    // Read the live session per request instead of closing over `token`: the
    // captured value is a snapshot from mount, and after a browser restart
    // MSAL is purged so `accounts` stays [] forever and this effect never
    // re-runs to replace it. Fall back to `token` (the value the effect above
    // already acquired via acquireAccessToken's MSAL/Google chain) when there
    // is no app session yet — never fall back to '', which would send an
    // empty bearer despite a perfectly valid provider token being in hand.
    const adaptor = new AuthAdaptor(() => getAppSession()?.accessToken ?? token, {
      readUrl: readUrl ? `${API_BASE}${readUrl}` : undefined,
      transformResponse,
    })

    const dataOpts: DataOptions = {
      url: `${API_BASE}${url}`,
      adaptor,
      key,
      offline: false,
    }

    return new DataManager(dataOpts)
    // eslint-disable-next-line react-hooks/exhaustive-deps -- transformResponse is stable per call-site
  }, [token, url, key, readUrl])
}
