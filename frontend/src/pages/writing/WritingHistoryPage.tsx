import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Grid, Columns, Column, type ColumnTemplateProps } from '@syncfusion/react-grid'
import { useListWritingEntriesQuery } from '../../shared/api/api'
import type { WritingEntryDto } from '../../shared/api/writingTypes'
import { formatDateThai } from './formatDate'
import './WritingHistoryPage.css'

type FilterMode = 'all' | 'pending' | 'corrected'

const stripHtml = (html: string): string =>
  html
    .replace(/<[^>]*>/g, ' ')
    .replace(/&nbsp;|&#160;|&#xa0;/gi, ' ')
    .replace(/\s+/g, ' ')
    .trim()

function DateCell({ data }: ColumnTemplateProps<WritingEntryDto>) {
  return <span>{formatDateThai(data.date)}</span>
}

function TextPreviewCell({ data }: ColumnTemplateProps<WritingEntryDto>) {
  const preview = stripHtml(data.text)
  return <span className="writing-history-preview">{preview.length > 80 ? `${preview.slice(0, 80)}…` : preview}</span>
}

function StatusBadgeCell({ data }: ColumnTemplateProps<WritingEntryDto>) {
  return data.correctedAt ? (
    <span className="writing-history-badge writing-history-badge--corrected">🔒 ตรวจแล้ว</span>
  ) : (
    <span className="writing-history-badge writing-history-badge--pending">⏳ รอตรวจ</span>
  )
}

function OpenActionCell({ data }: ColumnTemplateProps<WritingEntryDto>) {
  const navigate = useNavigate()
  return (
    <button
      type="button"
      className="writing-history-open-btn"
      onClick={() => navigate(`/writing/history/${data.id}`)}
    >
      เปิด
    </button>
  )
}

export function WritingHistoryPage() {
  const navigate = useNavigate()
  const { data: entries, isLoading, isError } = useListWritingEntriesQuery()
  const [filterMode, setFilterMode] = useState<FilterMode>('all')

  const pendingCount = useMemo(
    () => (entries ?? []).filter((e) => !e.correctedAt).length,
    [entries],
  )

  const rows = useMemo(() => {
    const list = entries ?? []
    if (filterMode === 'pending') return list.filter((e) => !e.correctedAt)
    if (filterMode === 'corrected') return list.filter((e) => e.correctedAt)
    return list
  }, [entries, filterMode])

  return (
    <div className="writing-history-page">
      <button type="button" className="writing-history-back-btn" onClick={() => navigate('/writing')}>
        ← กลับ
      </button>
      <h1 className="writing-history-title">ประวัติ</h1>

      <div className="writing-history-filter-bar">
        <button
          type="button"
          className={
            filterMode === 'all' ? 'writing-history-chip writing-history-chip--active' : 'writing-history-chip'
          }
          onClick={() => setFilterMode('all')}
        >
          ทั้งหมด
        </button>
        <button
          type="button"
          className={
            filterMode === 'pending' ? 'writing-history-chip writing-history-chip--active' : 'writing-history-chip'
          }
          onClick={() => setFilterMode('pending')}
        >
          รอตรวจ{pendingCount > 0 ? ` (${pendingCount})` : ''}
        </button>
        <button
          type="button"
          className={
            filterMode === 'corrected' ? 'writing-history-chip writing-history-chip--active' : 'writing-history-chip'
          }
          onClick={() => setFilterMode('corrected')}
        >
          ตรวจแล้ว
        </button>
      </div>

      {isLoading && <div className="writing-history-status">กำลังโหลด...</div>}
      {isError && <div className="writing-history-status writing-history-status--error">โหลดไม่สำเร็จ</div>}
      {!isLoading && !isError && rows.length === 0 && (
        <div className="writing-history-status">
          {filterMode !== 'all' && (entries?.length ?? 0) > 0
            ? 'ไม่มีรายการที่ตรงกับตัวกรองนี้'
            : 'ยังไม่มีรายการ'}
        </div>
      )}

      {!isLoading && !isError && rows.length > 0 && (
        <Grid
          key={filterMode}
          dataSource={rows}
          pageSettings={{ enabled: true, pageSize: 20, currentPage: 1 }}
        >
          <Columns>
            <Column field="date" headerText="วันที่" width="110" template={DateCell} />
            <Column field="text" headerText="ข้อความ" template={TextPreviewCell} />
            <Column field="correctedAt" headerText="สถานะ" width="120" template={StatusBadgeCell} />
            <Column field="id" headerText="เปิด" width="80" template={OpenActionCell} />
          </Columns>
        </Grid>
      )}
    </div>
  )
}
