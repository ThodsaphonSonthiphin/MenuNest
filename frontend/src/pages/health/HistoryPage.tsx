import { useNavigate } from 'react-router-dom'
import { EpisodeListItem } from './components/EpisodeListItem'
import { FilterChip } from './components/FilterChip'
import { useEpisodeHistory } from './hooks/useEpisodeHistory'
import './styles/health.css'

/**
 * History — chronological list of all episodes, grouped by date.
 *
 *  - Filter chips toggle date range + outcome flags. We keep the
 *    interaction stateless on the URL for Phase 1 (no deep links yet)
 *    because the filter set will keep evolving.
 *  - Search is client-side, matching `symptomName` + `firstDrugName`.
 *    The backend doesn't have a `q=` parameter yet; adding it is in the
 *    Phase 2 plan.
 *
 * Mock: docs/mocks/patient-history-mock.html (left phone).
 */
export function HistoryPage() {
  const navigate = useNavigate()
  const { filter, setFilter, groups, totalCount, isLoading, isError } =
    useEpisodeHistory()

  const handleEpisodeClick = (id: string) => {
    navigate(`/health/episode/${id}`)
  }

  return (
    <div className="health-page">
      <div className="health-page__container">
        <header className="health-header">
          <div className="health-header__user" style={{ fontSize: 15 }}>
            <button
              type="button"
              className="health-icon-btn"
              aria-label="Back"
              onClick={() => navigate('/health')}
            >
              ←
            </button>
            <span>ประวัติ</span>
          </div>
          <div className="health-header__icons">
            {/* Sort + export are Phase 2 — wired as visual stubs. */}
            <button
              type="button"
              className="health-icon-btn"
              aria-label="Sort"
              title="เรียงลำดับ (Phase 2)"
              disabled
            >
              ⇅
            </button>
            <button
              type="button"
              className="health-icon-btn"
              aria-label="Share / Export"
              onClick={() => navigate('/health/share')}
              title="แชร์ให้หมอ"
            >
              📤
            </button>
          </div>
        </header>

        <input
          type="text"
          className="health-search-box"
          placeholder="ค้นหา (อาการ, ยา...)"
          value={filter.search}
          onChange={(e) => setFilter({ search: e.target.value })}
        />

        <div className="health-filter-bar">
          <FilterChip
            label="📅 30 วัน"
            active={filter.rangePreset === 'last30'}
            onClick={() => setFilter({ rangePreset: 'last30' })}
          />
          <FilterChip
            label="ทั้งหมด"
            active={filter.rangePreset === 'all'}
            onClick={() => setFilter({ rangePreset: 'all' })}
          />
          <FilterChip
            label="✅ resolved"
            active={filter.onlyResolved}
            onClick={() =>
              setFilter({
                onlyResolved: !filter.onlyResolved,
                // Resolved and failed are mutually exclusive on the
                // backend (`onlyFailed` filters opposite condition).
                onlyFailed: filter.onlyResolved ? filter.onlyFailed : false,
              })
            }
          />
          <FilterChip
            label="⚠️ failed"
            active={filter.onlyFailed}
            onClick={() =>
              setFilter({
                onlyFailed: !filter.onlyFailed,
                onlyResolved: filter.onlyFailed ? filter.onlyResolved : false,
              })
            }
          />
          <FilterChip
            label="⚭ period"
            active={filter.periodOnly}
            onClick={() => setFilter({ periodOnly: !filter.periodOnly })}
          />
          <FilterChip
            label="🌀 aura"
            active={filter.auraOnly}
            onClick={() => setFilter({ auraOnly: !filter.auraOnly })}
          />
        </div>

        {isLoading && (
          <div style={{ padding: 32, color: 'var(--hl-text-muted)', fontSize: 13 }}>
            กำลังโหลด...
          </div>
        )}

        {isError && (
          <div className="health-card" style={{ color: 'var(--hl-danger)' }}>
            เกิดข้อผิดพลาด กรุณาลองใหม่
          </div>
        )}

        {!isLoading && !isError && totalCount === 0 && (
          <div className="health-empty-state">
            ยังไม่มี episodes ในช่วงนี้
          </div>
        )}

        {groups.map((g) => (
          <section key={g.dateKey}>
            <div className="health-date-section-title">
              <span>{g.label}</span>
              <span className="health-date-section-count">
                {g.episodes.length} episodes
              </span>
            </div>
            {g.episodes.map((ep) => (
              <EpisodeListItem
                key={ep.id}
                episode={ep}
                onClick={handleEpisodeClick}
              />
            ))}
          </section>
        ))}

        {!isLoading && totalCount > 0 && filter.rangePreset !== 'all' && (
          <div className="health-empty-state">
            ... ดูประวัติเก่ากว่านี้กด "ทั้งหมด"
          </div>
        )}
      </div>
    </div>
  )
}
