import type {ReviewLink} from '../../../shared/api/api'

export const MAX_REVIEW_LINKS = 10

export function isValidReviewUrl(url: string): boolean {
  try {
    const u = new URL(url.trim())
    return u.protocol === 'http:' || u.protocol === 'https:'
  } catch {
    return false
  }
}

export function reviewHost(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./, '')
  } catch {
    return ''
  }
}

export function reviewLabel(link: ReviewLink, index: number): string {
  const trimmed = link.label?.trim()
  return trimmed && trimmed.length > 0 ? trimmed : `ดูรีวิว ${index + 1}`
}

export type ReviewDraft = {url: string; label: string}

export function sanitizeReviewDrafts(drafts: ReviewDraft[]): ReviewLink[] {
  return drafts
    .map((d) => ({url: d.url.trim(), label: d.label.trim()}))
    .filter((d) => d.url.length > 0)
    .map((d) => ({url: d.url, label: d.label.length > 0 ? d.label : null}))
}

export function draftsValid(drafts: ReviewDraft[]): boolean {
  const urls = drafts.map((d) => d.url.trim()).filter((u) => u.length > 0)
  return urls.length <= MAX_REVIEW_LINKS && urls.every(isValidReviewUrl)
}
