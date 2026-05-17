import { useMemo } from 'react'
import { MultiSelect } from '@syncfusion/react-dropdowns'
import type { SearchAutoCompleteItem } from './SearchAutoComplete'
import {
  extractCustomText,
  isCustomValue,
  makeCustomValue,
} from './customValueSentinel'

/**
 * Multi-select counterpart to `SearchAutoComplete`.
 *
 * The contract mirrors the single-select version:
 *  - `value: string[]` is an array of ids (or `__custom:<text>__`
 *    sentinels for entries the user typed in and the parent hasn't
 *    materialised yet),
 *  - `onChange(ids: string[])` fires with the new array on any change,
 *  - `allowCustom` enables the Syncfusion `customValue` flag so users
 *    can press Enter on free-form text to add a new tag.
 *
 * The caller decides what to do with custom sentinels (typically: on
 * submit, create-on-the-fly via the matching mutation and replace the
 * sentinel with the returned id).
 */
export interface SearchMultiSelectProps {
  dataSource: SearchAutoCompleteItem[]
  value: string[]
  onChange: (ids: string[]) => void
  placeholder?: string
  allowCustom?: boolean
  disabled?: boolean
  id?: string
}

export function SearchMultiSelect({
  dataSource,
  value,
  onChange,
  placeholder,
  allowCustom = false,
  disabled = false,
  id,
}: SearchMultiSelectProps) {
  // Two-way mapping between ids and display names. We bind the dropdown
  // to `name` (string) so Syncfusion's internal filter, custom-value,
  // and rendering all work on plain strings. We convert back to ids in
  // the change handler.
  const items = useMemo(
    () => dataSource.map((d) => ({ id: d.id, name: d.name })),
    [dataSource],
  )

  const idByName = useMemo(() => {
    const m = new Map<string, string>()
    for (const it of items) m.set(it.name, it.id)
    return m
  }, [items])

  const nameById = useMemo(() => {
    const m = new Map<string, string>()
    for (const it of items) m.set(it.id, it.name)
    return m
  }, [items])

  // Convert the parent's `string[]` ids into the array of display strings
  // the MultiSelect expects.
  const displayValue = useMemo<string[]>(
    () =>
      value.map((v) =>
        isCustomValue(v) ? extractCustomText(v) : (nameById.get(v) ?? v),
      ),
    [value, nameById],
  )

  return (
    <MultiSelect
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
      hideSelectedItem
      onChange={(args: { value: unknown }) => {
        const next = Array.isArray(args.value) ? (args.value as unknown[]) : []
        const mapped: string[] = []
        for (const item of next) {
          if (typeof item !== 'string') continue
          const matchId = idByName.get(item)
          if (matchId) {
            mapped.push(matchId)
          } else if (allowCustom) {
            mapped.push(makeCustomValue(item))
          }
        }
        onChange(mapped)
      }}
      onCustomValueSelect={(text: string) => {
        const trimmed = text.trim()
        if (!trimmed) return
        // Append the new custom entry to the current ids. Syncfusion will
        // also fire onChange next, but we update eagerly here so the
        // caller has a stable view of the sentinel value even if the
        // change event order varies between versions.
        const next = [...value, makeCustomValue(trimmed)]
        onChange(next)
      }}
    />
  )
}
