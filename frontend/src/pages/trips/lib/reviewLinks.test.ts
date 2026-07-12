import {describe, expect, it} from 'vitest'
import {
  MAX_REVIEW_LINKS,
  isValidReviewUrl,
  reviewHost,
  reviewLabel,
  sanitizeReviewDrafts,
  draftsValid,
} from './reviewLinks'

describe('reviewLinks', () => {
  it('accepts http(s) URLs and rejects others', () => {
    expect(isValidReviewUrl('https://www.tiktok.com/@u/video/1')).toBe(true)
    expect(isValidReviewUrl('http://x.com')).toBe(true)
    expect(isValidReviewUrl('ftp://x.com')).toBe(false)
    expect(isValidReviewUrl('not a url')).toBe(false)
    expect(isValidReviewUrl('')).toBe(false)
  })

  it('extracts host without www', () => {
    expect(reviewHost('https://www.tiktok.com/@u/1')).toBe('tiktok.com')
    expect(reviewHost('https://youtu.be/x')).toBe('youtu.be')
    expect(reviewHost('garbage')).toBe('')
  })

  it('falls back to a numbered label when blank', () => {
    expect(reviewLabel({url: 'https://x.com', label: '@foodie'}, 0)).toBe('@foodie')
    expect(reviewLabel({url: 'https://x.com', label: null}, 0)).toBe('ดูรีวิว 1')
    expect(reviewLabel({url: 'https://x.com', label: '   '}, 1)).toBe('ดูรีวิว 2')
  })

  it('sanitize trims, drops blank-url rows, nulls blank labels', () => {
    const out = sanitizeReviewDrafts([
      {url: '  https://x.com/1 ', label: '  one '},
      {url: '   ', label: 'ignored'},
      {url: 'https://x.com/2', label: ''},
    ])
    expect(out).toEqual([
      {url: 'https://x.com/1', label: 'one'},
      {url: 'https://x.com/2', label: null},
    ])
  })

  it('draftsValid rejects invalid urls and over-cap counts', () => {
    expect(draftsValid([{url: 'https://x.com', label: ''}])).toBe(true)
    expect(draftsValid([{url: '', label: ''}])).toBe(true) // blank rows are dropped, not invalid
    expect(draftsValid([{url: 'nope', label: ''}])).toBe(false)
    const eleven = Array.from({length: MAX_REVIEW_LINKS + 1}, (_, i) => ({url: `https://x.com/${i}`, label: ''}))
    expect(draftsValid(eleven)).toBe(false)
  })
})
