import { useState, useEffect, useMemo } from 'react'
import { DataManager, WebApiAdaptor } from '@syncfusion/react-data'
import type { DataOptions } from '@syncfusion/react-data'
import { useMsal } from '@azure/msal-react'
import { apiScopes } from '../auth/msalConfig'

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'https://localhost:5001'

// ---------------------------------------------------------------------------
// Auth adaptor – exported so pages can extend it further
// ---------------------------------------------------------------------------

/**
 * WebApiAdaptor subclass that:
 * 1. Injects `Authorization: Bearer <token>` on every request.
 * 2. Sends PUT to `url/{id}` (REST convention) instead of plain `url`.
 * 3. Optionally rewrites the GET URL and transforms the GET response.
 */
export class AuthAdaptor extends WebApiAdaptor {
  private _token: string
  private _readUrl: string | undefined
  private _transformResponse: ((data: unknown) => unknown) | undefined

  constructor(
    token: string,
    opts?: {
      /** If provided, GET requests hit this URL instead of the base URL. */
      readUrl?: string
      /** Transform the raw GET response before the Grid receives it. */
      transformResponse?: (data: unknown) => unknown
    },
  ) {
    super()
    this._token = token
    this._readUrl = opts?.readUrl
    this._transformResponse = opts?.transformResponse
  }

  override beforeSend(dm: DataManager, request: Request, settings?: unknown): void {
    super.beforeSend(dm, request, settings as never)
    request.headers.set('Authorization', `Bearer ${this._token}`)
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
    const base = super.processResponse(
      data as never, ds as never, query as never, xhr, request as never, changes as never,
    )
    if (this._transformResponse && request && (request as { type?: string }).type?.toUpperCase() !== 'POST') {
      return this._transformResponse(base) as object
    }
    return base as object
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
 * The DataManager is recreated whenever the access token changes, so the
 * Grid always sends a valid Bearer header. Returns `null` until the first
 * token is acquired.
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
    if (accounts.length === 0 || apiScopes.length === 0) return

    let cancelled = false
    instance
      .acquireTokenSilent({ scopes: apiScopes, account: accounts[0] })
      .then((r) => {
        if (!cancelled) setToken(r.accessToken)
      })
      .catch(() => {
        /* token failure is handled elsewhere (MSAL redirect) */
      })
    return () => {
      cancelled = true
    }
  }, [instance, accounts])

  return useMemo(() => {
    if (!token) return null

    const adaptor = new AuthAdaptor(token, {
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
