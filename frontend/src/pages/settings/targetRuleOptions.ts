// The active target grammar rule the AI correction loop grades against.
// The writer flips it by hand — there is no calendar rotation (rule-rotation).
// Kept as free text with presets rather than an enum: the rule is a teaching
// choice, not a system value, and the server only bounds its length.

/** Matches UserSettings.ActiveTargetRule / WritingEntries.TargetRule nvarchar(200). */
export const MAX_TARGET_RULE_LENGTH = 200

export const TARGET_RULE_PRESETS = [
  'third-person singular -s',
  'articles (a/an/the)',
  'past simple -ed',
  'plural -s',
] as const

/**
 * Trims, collapses a blank rule to null (which clears it server-side), and
 * caps at the column ceiling so the PUT cannot be rejected for length.
 */
export function normalizeTargetRule(rule: string | null | undefined): string | null {
  const trimmed = (rule ?? '').trim()
  if (trimmed.length === 0) return null
  return trimmed.slice(0, MAX_TARGET_RULE_LENGTH)
}
