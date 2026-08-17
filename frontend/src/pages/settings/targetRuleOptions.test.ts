import { describe, expect, it } from 'vitest'
import { TARGET_RULE_PRESETS, normalizeTargetRule, MAX_TARGET_RULE_LENGTH } from './targetRuleOptions'

describe('targetRuleOptions', () => {
  it('offers the presets the correction loop is written around', () => {
    expect(TARGET_RULE_PRESETS).toContain('third-person singular -s')
    expect(TARGET_RULE_PRESETS).toContain('articles (a/an/the)')
    expect(TARGET_RULE_PRESETS.length).toBeGreaterThanOrEqual(3)
  })

  it('trims a rule', () => {
    expect(normalizeTargetRule('  plural -s  ')).toBe('plural -s')
  })

  it('turns a blank rule into null so the server clears it', () => {
    expect(normalizeTargetRule('')).toBeNull()
    expect(normalizeTargetRule('   ')).toBeNull()
    expect(normalizeTargetRule(null)).toBeNull()
  })

  it('truncates at the 200-char server ceiling instead of sending a rejected value', () => {
    const long = 'x'.repeat(250)

    const result = normalizeTargetRule(long)

    expect(result).not.toBeNull()
    expect(result!.length).toBe(MAX_TARGET_RULE_LENGTH)
  })
})
