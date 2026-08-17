import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  RichTextEditorComponent,
  Inject,
  Toolbar,
  Link,
  HtmlEditor,
  QuickToolbar,
  type RichTextEditorComponent as RteInstance,
} from '@syncfusion/ej2-react-richtexteditor'
import {
  useListWritingEntriesQuery,
  useUpdateWritingEntryTextMutation,
  useDeleteWritingEntryMutation,
} from '../../shared/api/api'
import { formatDateThai } from './formatDate'
import { saveErrorMessage } from './saveErrorMessage'
import './WritingHistoryPage.css'
import './WritingEntryDetailPage.css'

export function WritingEntryDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  // Poll while this page is open: a correction can only arrive over MCP (from
  // the writer's Claude Code), so without polling the page keeps a stale
  // correctedAt and offers an edit the server will refuse (WMCP-26).
  const { data: entries, isLoading, isError } = useListWritingEntriesQuery(undefined, {
    pollingInterval: 15_000,
  })
  const [updateText, { isLoading: isSaving }] = useUpdateWritingEntryTextMutation()
  const [deleteEntry, { isLoading: isDeleting }] = useDeleteWritingEntryMutation()
  const rteRef = useRef<RteInstance | null>(null)
  const [isEditing, setIsEditing] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const entry = useMemo(() => entries?.find((e) => e.id === id), [entries, id])
  const isLocked = Boolean(entry?.correctedAt)

  // A correction that lands while editing locks the text under us. Drop out of
  // edit mode immediately and say so — the writer's unsaved typing is lost,
  // which is the accepted trade (2026-08-17) for never offering an edit that
  // cannot be saved.
  useEffect(() => {
    if (isLocked && isEditing) {
      setIsEditing(false)
      setError('คืนนี้เพิ่งถูกตรวจแล้ว — แก้ข้อความไม่ได้อีก')
    }
  }, [isLocked, isEditing])

  const handleSave = async () => {
    if (!entry) return
    const html = rteRef.current?.getHtml() ?? ''
    setError(null)
    try {
      await updateText({ id: entry.id, text: html }).unwrap()
      setIsEditing(false)
    } catch (err) {
      console.error('updateWritingEntryText failed', err)
      // A correction can land while this PUT is in flight, so the page's own
      // correctedAt may still be stale here. The server's 400 is the only
      // signal that is never stale -- "try again" would be a lie.
      setError(saveErrorMessage(err))
    }
  }

  const handleDelete = async () => {
    if (!entry) return
    setError(null)
    try {
      await deleteEntry(entry.id).unwrap()
      navigate('/writing/history')
    } catch (err) {
      console.error('deleteWritingEntry failed', err)
      setError('ลบไม่สำเร็จ ลองอีกครั้ง')
    }
  }

  if (isLoading) {
    return <div className="writing-detail-page writing-detail-status">กำลังโหลด...</div>
  }

  if (isError) {
    return (
      <div className="writing-detail-page">
        <button type="button" className="writing-detail-back-btn" onClick={() => navigate('/writing/history')}>
          ← กลับ
        </button>
        <div className="writing-history-status writing-history-status--error">โหลดไม่สำเร็จ</div>
      </div>
    )
  }

  if (!entry) {
    return (
      <div className="writing-detail-page">
        <button type="button" className="writing-detail-back-btn" onClick={() => navigate('/writing/history')}>
          ← กลับ
        </button>
        <div className="writing-detail-status">ไม่พบรายการนี้ (อาจถูกลบไปแล้ว)</div>
      </div>
    )
  }

  return (
    <div className="writing-detail-page">
      <button type="button" className="writing-detail-back-btn" onClick={() => navigate('/writing/history')}>
        ← กลับ
      </button>

      <div className="writing-detail-header">
        <span className="writing-detail-date">{formatDateThai(entry.date)}</span>
        <span
          className={
            isLocked
              ? 'writing-history-badge writing-history-badge--corrected'
              : 'writing-history-badge writing-history-badge--pending'
          }
        >
          {isLocked ? '🔒 ตรวจแล้ว' : '⏳ รอตรวจ'}
        </span>
      </div>

      {isEditing ? (
        <RichTextEditorComponent
          ref={rteRef}
          height={300}
          value={entry.text}
          toolbarSettings={{ items: ['Bold', 'Italic', 'Underline', 'OrderedList', 'UnorderedList'] }}
        >
          <Inject services={[Toolbar, Link, HtmlEditor, QuickToolbar]} />
        </RichTextEditorComponent>
      ) : (
        // Trusted content: this HTML is the signed-in user's own writing,
        // authored by the same Syncfusion RTE that produced it (WritingPage) --
        // no third party ever supplies this string.
        <div className="writing-detail-text" dangerouslySetInnerHTML={{ __html: entry.text }} />
      )}

      {error && <div className="writing-detail-error">{error}</div>}

      <div className="writing-detail-actions">
        {isLocked ? (
          <div className="writing-detail-locked-note">ตรวจแล้ว — แก้ข้อความไม่ได้ (ลบทั้งรายการได้)</div>
        ) : isEditing ? (
          <>
            <button type="button" className="writing-detail-save-btn" onClick={handleSave} disabled={isSaving}>
              บันทึก
            </button>
            <button
              type="button"
              className="writing-detail-cancel-btn"
              onClick={() => {
                setIsEditing(false)
                setError(null)
              }}
            >
              ยกเลิก
            </button>
          </>
        ) : (
          <button type="button" className="writing-detail-edit-btn" onClick={() => setIsEditing(true)}>
            แก้ไข
          </button>
        )}

        {confirmingDelete ? (
          <span className="writing-detail-confirm-delete">
            ลบรายการนี้แน่ใจไหม?
            <button type="button" className="writing-detail-confirm-yes" onClick={handleDelete} disabled={isDeleting}>
              ลบ
            </button>
            <button
              type="button"
              className="writing-detail-confirm-no"
              onClick={() => {
                setConfirmingDelete(false)
                setError(null)
              }}
            >
              ยกเลิก
            </button>
          </span>
        ) : (
          <button type="button" className="writing-detail-delete-btn" onClick={() => setConfirmingDelete(true)}>
            ลบ
          </button>
        )}
      </div>
    </div>
  )
}
