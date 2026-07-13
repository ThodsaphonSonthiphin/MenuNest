// Pure helpers for the Place checklist (issue #23). No api.ts import so this is
// unit-testable in isolation (the SPA vitest runs in node env, no jsdom).

export const MAX_CHECKLIST_ITEMS_PER_PLACE = 20
export const MAX_CHECKLIST_NAME = 100

export function normalizeChecklistName(raw: string): string {
  return raw.trim().replace(/\s+/g, ' ')
}

export function isValidChecklistName(raw: string): boolean {
  const n = normalizeChecklistName(raw)
  return n.length > 0 && n.length <= MAX_CHECKLIST_NAME
}

export function matchLibrary<T extends {name: string}>(query: string, items: T[]): T[] {
  const q = normalizeChecklistName(query).toLowerCase()
  if (q.length === 0) return items
  return items.filter((i) => i.name.toLowerCase().includes(q))
}

export function exactMatch<T extends {name: string}>(query: string, items: T[]): T | null {
  const q = normalizeChecklistName(query).toLowerCase()
  return items.find((i) => i.name.toLowerCase() === q) ?? null
}

export function checklistProgress(entries: {isChecked: boolean}[]): {done: number; total: number} {
  return {done: entries.filter((e) => e.isChecked).length, total: entries.length}
}
