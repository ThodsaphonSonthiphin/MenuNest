import { useMemo } from 'react'
import { Autocomplete } from '@syncfusion/react-dropdowns'
import {
  extractCustomText,
  isCustomValue,
  makeCustomValue,
} from './customValueSentinel'

export interface SearchAutoCompleteItem {
  id: string
  name: string
}

export interface SearchAutoCompleteProps {
  dataSource: SearchAutoCompleteItem[]
  /** Selected id, or `null` when nothing is picked. */
  value: string | null
  /**
   * Fires with the new id. For custom entries (only when `allowCustom`),
   * fires with the sentinel form — use `isCustomValue` +
   * `extractCustomText` (from `./customValueSentinel`) to detect/parse.
   */
  onChange: (id: string | null) => void
  placeholder?: string
  /**
   * When true, users can press Enter on free-form text to commit a value
   * not in `dataSource`. The committed value is delivered via `onChange`
   * wrapped in the `__custom:<text>__` sentinel.
   *
   * Defaults to `false` (closed list).
   */
  allowCustom?: boolean
  disabled?: boolean
  /**
   * Optional id for label-targeting / accessibility tests.
   */
  id?: string
}

/**
 * Thin wrapper around Syncfusion's `<Autocomplete>` that:
 *  - normalises the `dataSource` to `{ id, name }` so callers don't have
 *    to think about `fields={{ text, value }}`,
 *  - exposes a single `value: string | null` + `onChange(id|null)`
 *    contract (no SyntheticEvent leaking out of the component),
 *  - opts into Syncfusion's `customValue` + `onCustomValueSelect` for
 *    `allowCustom` and wraps the typed text in a sentinel format so the
 *    parent can detect "user typed something new" and persist it.
 *
 * The underlying component supports filterType=Contains for substring
 * matching, sized `Medium`, and `ignoreCase=true` — defaults we want
 * across the health module's three pickers.
 */
export function SearchAutoComplete({
  dataSource,
  value,
  onChange,
  placeholder,
  allowCustom = false,
  disabled = false,
  id,
}: SearchAutoCompleteProps) {
  // Map id -> item once per render so the change handler can look up
  // a previously-selected entry by name (Syncfusion sends back the value
  // we mapped, not the original id).
  const items = useMemo(
    () =>
      dataSource.map((d) => ({
        id: d.id,
        name: d.name,
      })),
    [dataSource],
  )

  // Convert the parent's id back into the matching name string the
  // Autocomplete renders. Custom sentinels render their typed text so
  // the user can keep editing.
  const displayValue = useMemo<string | null>(() => {
    if (value == null) return null
    if (isCustomValue(value)) return extractCustomText(value)
    return items.find((i) => i.id === value)?.name ?? null
  }, [items, value])

  return (
    <Autocomplete
      id={id}
      dataSource={items}
      fields={{ text: 'name', value: 'name' }}
      value={displayValue}
      placeholder={placeholder}
      disabled={disabled}
      clearButton
      filterType="Contains"
      ignoreCase
      customValue={allowCustom}
      onChange={(e: { value: unknown }) => {
        const picked = (e.value ?? null) as string | null
        if (picked == null || picked === '') {
          onChange(null)
          return
        }
        const match = items.find((i) => i.name === picked)
        if (match) {
          onChange(match.id)
        } else if (allowCustom) {
          // Free-form text selected (e.g. via blur retaining typed value).
          onChange(makeCustomValue(picked))
        } else {
          onChange(null)
        }
      }}
      onCustomValueSelect={(text: string) => {
        const trimmed = text.trim()
        if (!trimmed) {
          onChange(null)
          return
        }
        // The user committed free-form text via Enter. Always emit the
        // sentinel so the caller can decide whether to materialise it.
        onChange(makeCustomValue(trimmed))
      }}
    />
  )
}
