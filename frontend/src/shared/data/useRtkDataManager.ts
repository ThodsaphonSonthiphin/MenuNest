import { useCallback, useMemo, useRef } from 'react'
import { DataManager, JsonAdaptor } from '@syncfusion/react-data'

// ---------------------------------------------------------------------------
// A Syncfusion DataManager that reads from RTK Query cache and writes
// through RTK Query mutations via Grid's onDataChangeStart event.
//
// Data flow:
//   Read:  Server → RTK Query cache → JsonAdaptor → Grid renders
//   Write: Grid CRUD → onDataChangeStart (e.cancel=true) → mutation
//          → RTK Query → Server → cache invalidates → data updates
//          → DataManager recreated → Grid refreshes
// ---------------------------------------------------------------------------

export interface UseRtkDataManagerOptions<T> {
  /** Primary key field name. @default 'id' */
  key?: string
  /** Called when the Grid adds a new row (toolbar Add → Update). */
  onAdd?: (row: T) => Promise<void>
  /** Called when the Grid saves an edited row. */
  onUpdate?: (row: T) => Promise<void>
  /** Called when the Grid deletes row(s) (toolbar Delete). */
  onDelete?: (rows: T[]) => Promise<void>
}

export interface UseRtkDataManagerResult {
  /** DataManager to pass as Grid's dataSource. null until data loads. */
  dm: DataManager | null
  /**
   * Wire this to Grid's `onDataChangeStart` prop. It calls `e.cancel = true`
   * and dispatches the appropriate RTK Query mutation based on `e.action`.
   */
  onDataChangeStart: (e: { action: string; data: unknown; cancel?: boolean }) => void
}

/**
 * Wraps RTK Query data in a Syncfusion `DataManager` (JsonAdaptor) so the
 * Grid can use its built-in toolbar Add/Edit/Delete/Update/Cancel features
 * while RTK Query handles all server communication + caching.
 *
 * Returns `{ dm, onDataChangeStart }`. `dm` is `null` until `data` is
 * defined — use this as a guard so the Grid doesn't mount with empty data.
 *
 * @example
 * ```tsx
 * const { data } = useListIngredientsQuery()
 * const [create] = useCreateIngredientMutation()
 * const { dm, onDataChangeStart } = useRtkDataManager(data, {
 *   onAdd: (row) => create({ name: row.name, unit: row.unit }).unwrap(),
 * })
 * if (!dm) return <p>Loading…</p>
 * <Grid dataSource={dm} onDataChangeStart={onDataChangeStart} ...>
 * ```
 */
export function useRtkDataManager<T>(
  data: T[] | undefined,
  options?: UseRtkDataManagerOptions<T>,
): UseRtkDataManagerResult {
  const { key = 'id', onAdd, onUpdate, onDelete } = options ?? {}

  // Keep callbacks in a ref so onDataChangeStart doesn't need to be
  // recreated when callbacks change (they often capture closures).
  const cbRef = useRef({ onAdd, onUpdate, onDelete })
  cbRef.current = { onAdd, onUpdate, onDelete }

  const dm = useMemo(() => {
    if (!data) return null
    return new DataManager({
      json: data as unknown as object[],
      adaptor: new JsonAdaptor(),
      key,
    } as never)
  }, [data, key])

  const onDataChangeStart = useCallback(
    (e: { action: string; data: unknown; cancel?: boolean }) => {
      // Prevent the Grid from modifying its local dataSource — RTK Query
      // cache invalidation will trigger a re-render with fresh data.
      e.cancel = true

      if (e.action === 'Add') {
        cbRef.current.onAdd?.(e.data as T)
      } else if (e.action === 'Edit') {
        cbRef.current.onUpdate?.(e.data as T)
      } else if (e.action === 'Delete') {
        const rows = Array.isArray(e.data) ? e.data : [e.data]
        cbRef.current.onDelete?.(rows as T[])
      }
    },
    [],
  )

  return { dm, onDataChangeStart }
}
