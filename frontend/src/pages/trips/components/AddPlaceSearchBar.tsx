// frontend/src/pages/trips/components/AddPlaceSearchBar.tsx
// Floating search bar over the map (ADR-016). Live suggestions from usePlaceSearch;
// the "วางลิงก์" control reveals the hidden paste fallback. Matches the mock.
import type {Suggestion} from '../hooks/usePlaceSearch'

export interface AddPlaceSearchBarProps {
  query: string
  onQueryChange(q: string): void
  suggestions: Suggestion[]
  loading: boolean
  error: string | null
  onPick(placeId: string): void
  onOpenLinkFallback(): void
  onClose(): void
  autoFocus?: boolean
  bannerOffset?: boolean
}

export function AddPlaceSearchBar({
  query, onQueryChange, suggestions, loading, error, onPick, onOpenLinkFallback, onClose, autoFocus, bannerOffset,
}: AddPlaceSearchBarProps) {
  return (
    <div className={`add-search-wrap${bannerOffset ? ' add-search-wrap--banner' : ''}`}>
      <div className="add-search-box">
        <svg className="add-search-mag" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"><circle cx="11" cy="11" r="7" /><path d="M21 21l-4-4" /></svg>
        <input
          className="add-search-input"
          value={query}
          onChange={(e) => onQueryChange(e.target.value)}
          placeholder="ค้นหาสถานที่…"
          autoFocus={autoFocus}
        />
        <button type="button" className="add-search-link" onClick={onOpenLinkFallback}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1" /><path d="M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1" /></svg>
          วางลิงก์
        </button>
        <button type="button" className="add-search-close" onClick={onClose} aria-label="ปิดการค้นหา">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"><path d="M6 6l12 12" /><path d="M18 6L6 18" /></svg>
        </button>
      </div>

      {!query && suggestions.length === 0 && !loading && !error && (
        <div className="add-search-hint">หรือแตะหมุดบนแผนที่เพื่อเพิ่ม</div>
      )}

      {(suggestions.length > 0 || error) && (
        <div className="add-suggest">
          {error && <div className="add-suggest-error">{error}</div>}
          {suggestions.map((s) => (
            <button type="button" key={s.placeId} className="add-sug" onClick={() => onPick(s.placeId)}>
              <svg className="add-sug-mkr" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 21s-7-6.3-7-11a7 7 0 0 1 14 0c0 4.7-7 11-7 11z" /><circle cx="12" cy="10" r="2.5" /></svg>
              <span className="add-sug-txt"><b>{s.primary}</b><span>{s.secondary}</span></span>
            </button>
          ))}
        </div>
      )}
      {loading && suggestions.length === 0 && <div className="add-suggest add-suggest-loading">กำลังค้นหา…</div>}
    </div>
  )
}
