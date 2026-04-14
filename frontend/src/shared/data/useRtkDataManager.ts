import { useMemo, useRef } from 'react'
import { DataManager, JsonAdaptor } from '@syncfusion/react-data'

// ---------------------------------------------------------------------------
// A Syncfusion DataManager that reads from RTK Query cache and writes
// through RTK Query mutations. The Grid gets built-in toolbar CRUD while
// RTK Query stays the single source of truth for server data + auth.
//
// Data flow:
//   Read:  Server → RTK Query cache → JsonAdaptor → Grid renders
//   Write: Grid CRUD → RtkAdaptor → mutation callback → RTK Query → Server
//          → cache invalidates → data prop updates → DataManager recreated
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

/**
 * Wraps RTK Query data in a Syncfusion `DataManager` so the Grid can
 * use its built-in toolbar Add/Edit/Delete/Update/Cancel features while
 * RTK Query handles all server communication + caching.
 *
 * Returns `null` until `data` is defined (so the Grid doesn't mount
 * with empty data — which would break edit.params.dataSource on
 * DropDownEdit columns that also use this pattern).
 *
 * @example
 * ```tsx
 * const { data } = useListIngredientsQuery()
 * const [create] = useCreateIngredientMutation()
 * const dm = useRtkDataManager(data, {
 *   onAdd: (row) => create({ name: row.name, unit: row.unit }).unwrap(),
 * })
 * if (!dm) return <p>Loading…</p>
 * <Grid dataSource={dm} ...>
 * ```
 */
export function useRtkDataManager<T extends Record<string, unknown>>(
  data: T[] | undefined,
  options?: UseRtkDataManagerOptions<T>,
): DataManager | null {
  const { key = 'id', onAdd, onUpdate, onDelete } = options ?? {}

  // Keep callbacks in a ref so the DataManager doesn't need to be
  // recreated when only the callbacks change (they often capture
  // closures that update every render).
  const cbRef = useRef({ onAdd, onUpdate, onDelete })
  cbRef.current = { onAdd, onUpdate, onDelete }

  return useMemo(() => {
    if (!data) return null

    // Custom JsonAdaptor subclass that intercepts CUD operations and
    // delegates to the RTK Query mutations via the ref'd callbacks.
    class RtkAdaptor extends JsonAdaptor {
      override insert(
        dm: DataManager,
        newRow: object,
        _tableName?: string,
        _query?: unknown,
      ): object {
        // Fire-and-forget the async mutation — the RTK Query cache
        // invalidation will cause `data` to update, which recreates
        // the DataManager and refreshes the Grid.
        cbRef.current.onAdd?.(newRow as T)
        return super.insert(dm, newRow)
      }

      override update(
        dm: DataManager,
        _keyField: string,
        value: object,
        _tableName?: string,
      ): object {
        cbRef.current.onUpdate?.(value as T)
        return super.update(dm, _keyField, value)
      }

      override remove(
        dm: DataManager,
        _keyField: string,
        value: object,
        _tableName?: string,
      ): object {
        // Syncfusion passes the deleted row's key value (not the full
        // object) for single delete. For batch delete it's an array.
        // We normalise to T[] for the callback.
        const rows = Array.isArray(value) ? value : [value]
        cbRef.current.onDelete?.(rows as T[])
        return super.remove(dm, _keyField, value)
      }
    }

    return new DataManager({
      json: data as unknown as object[],
      adaptor: new RtkAdaptor(),
      key,
    } as never)
  }, [data, key])
}
