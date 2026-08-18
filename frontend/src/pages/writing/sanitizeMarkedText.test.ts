// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { sanitizeMarkedText } from './sanitizeMarkedText'

describe('sanitizeMarkedText', () => {
  it('keeps a miss/fix pair exactly as the correction wrote it', () => {
    const input = '<p>She <span class="miss">go</span> <span class="fix">→ goes</span> home.</p>'
    expect(sanitizeMarkedText(input)).toBe(input)
  })

  it('keeps a hit span and a bracketed Thai span', () => {
    const input = '<p>Traffic <span class="hit">is</span> bad. <span class="th">[ข้าวต้ม]</span></p>'
    expect(sanitizeMarkedText(input)).toBe(input)
  })

  it('round-trips the real production marked text unchanged', () => {
    const input =
      '<p><span class="th">[วันนี้พาลูกสาวไปกินข้าวเย็น และกินซุซิสายพานกับภรรยา ที่ห้าง passione]</span></p>'
    expect(sanitizeMarkedText(input)).toBe(input)
  })

  it('removes a script tag and its contents', () => {
    const out = sanitizeMarkedText('<p>hello<script>alert(1)</script></p>')
    expect(out).toBe('<p>hello</p>')
    expect(out).not.toContain('alert')
  })

  it('removes an img with an onerror handler', () => {
    const out = sanitizeMarkedText('<p>hi <img src=x onerror="alert(1)"> there</p>')
    expect(out).not.toContain('<img')
    expect(out).not.toContain('onerror')
  })

  it('removes an anchor entirely, javascript: href and all', () => {
    const out = sanitizeMarkedText('<p><a href="javascript:alert(1)">tap</a></p>')
    expect(out).not.toContain('<a')
    expect(out).not.toContain('javascript:')
    expect(out).toContain('tap')
  })

  it('drops a class the app owns, keeping the element and its text', () => {
    const out = sanitizeMarkedText('<p><span class="writing-detail-delete-btn">go</span></p>')
    expect(out).toBe('<p><span>go</span></p>')
  })

  it('keeps only the allowed class when an unknown one is smuggled alongside', () => {
    const out = sanitizeMarkedText('<p><span class="miss evil">go</span></p>')
    expect(out).toBe('<p><span class="miss">go</span></p>')
  })

  it('strips a style attribute even on an allowed tag', () => {
    const out = sanitizeMarkedText('<p style="position:fixed;inset:0">hi</p>')
    expect(out).toBe('<p>hi</p>')
  })

  it('returns an empty string for an empty correction', () => {
    expect(sanitizeMarkedText('')).toBe('')
  })
})
