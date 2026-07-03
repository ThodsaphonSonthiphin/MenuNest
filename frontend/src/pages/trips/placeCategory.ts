// frontend/src/pages/trips/placeCategory.ts
// Single source of truth for how a place category is presented in the UI
// (emoji glyph, accent color, Thai label). Consolidates the maps that were
// previously duplicated across PlaceCard, ItineraryStopCard and the stop editor.
// Keys match the PlaceCategory union in shared/api/api.ts.
import type {PlaceCategory} from '../../shared/api/api'

export const CAT_EMOJI: Record<string, string> = {
  Stay: '🛏️',
  Eat: '🍜',
  See: '⛩️',
  Cafe: '☕',
  Shop: '🛍️',
  Other: '📍',
}

export const CAT_COLOR: Record<string, string> = {
  Stay: '#6d5ae6',
  Eat: '#e2553e',
  See: '#1f9d76',
  Cafe: '#b4791f',
  Shop: '#c2418f',
  Other: '#94a3b8',
}

export const CAT_LABEL: Record<string, string> = {
  Stay: 'ที่พัก',
  Eat: 'ร้านอาหาร',
  See: 'ที่เที่ยว',
  Cafe: 'คาเฟ่',
  Shop: 'ช้อปปิ้ง',
  Other: 'อื่นๆ',
}

export const catEmoji = (c: PlaceCategory | string): string => CAT_EMOJI[c] ?? '📍'
export const catColor = (c: PlaceCategory | string): string => CAT_COLOR[c] ?? '#94a3b8'
export const catLabel = (c: PlaceCategory | string): string => CAT_LABEL[c] ?? String(c)
