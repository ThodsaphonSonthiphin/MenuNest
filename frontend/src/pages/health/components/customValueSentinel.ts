/**
 * Helpers for the `__custom:<text>__` sentinel format used by
 * `SearchAutoComplete` / `SearchMultiSelect`. The sentinel lets callers
 * keep a single `string | string[]` id contract on their form state
 * while still flagging entries the user typed in (that haven't been
 * materialised on the server yet).
 *
 * Kept in a separate non-component module so React Refresh can fast-
 * reload the components above without losing state — the
 * `react-refresh/only-export-components` rule would otherwise flag the
 * component file for exporting plain functions.
 */
const CUSTOM_PREFIX = '__custom:'
const CUSTOM_SUFFIX = '__'

export function isCustomValue(id: string | null | undefined): id is string {
  if (typeof id !== 'string') return false
  return id.startsWith(CUSTOM_PREFIX) && id.endsWith(CUSTOM_SUFFIX)
}

export function extractCustomText(id: string): string {
  return id.slice(CUSTOM_PREFIX.length, id.length - CUSTOM_SUFFIX.length)
}

export function makeCustomValue(text: string): string {
  return `${CUSTOM_PREFIX}${text}${CUSTOM_SUFFIX}`
}
