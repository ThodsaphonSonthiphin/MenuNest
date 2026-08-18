import { useEffect, useRef, useState } from 'react'
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
  useGetWritingEntryQuery,
  useUpdateWritingEntryTextMutation,
  useDeleteWritingEntryMutation,
} from '../../shared/api/api'
import { formatDateThai } from './formatDate'
import { saveErrorMessage } from './saveErrorMessage'
import { loadErrorMessage } from './loadErrorMessage'
import { CorrectionResult } from './CorrectionResult'
import './WritingHistoryPage.css'
import './WritingEntryDetailPage.css'

export function WritingEntryDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [isEditing, setIsEditing] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Poll ONLY while the night is still pending: a correction can only arrive
  // over MCP, so an un-corrected page must notice it on its own (WMCP-26). Once
  // correctedAt is set the state is settled, and this payload carries a
  // markedText bounded at 50,000 characters — polling it forever buys nothing
  // (ADR-179). RTK Query honours a pollingInterval that changes between
  // renders, and 0 means "stop polling".
  const [pollingInterval, setPollingInterval] = useState(15_000)
  const { data: entry, isLoading, error: queryError } = useGetWritingEntryQuery(id!, {
    skip: !id,
    pollingInterval,
  })

  const isLocked = Boolean(entry?.correctedAt)

  useEffect(() => {
    if (entry?.correctedAt) setPollingInterval(0)
  }, [entry?.correctedAt])
  const rteRef = useRef<RteInstance | null>(null)

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

  const [updateText, { isLoading: isSaving }] = useUpdateWritingEntryTextMutation()
  const [deleteEntry, { isLoading: isDeleting }] = useDeleteWritingEntryMutation()

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

  if (!entry) {
    // A failed poll over data we already have must fall through instead of
    // landing here -- this branch is absence, not error (fix round 1, #97).
    // loadErrorMessage reads the ProblemDetails detail string the same way
    // saveErrorMessage does for the save path: ExceptionHandlingMiddleware
    // maps every DomainException (including a missing entry) to HTTP 400, so
    // a literal 404 here means the route itself is missing, not the entry --
    // that is a transient condition a retry fixes, so it gets the generic
    // copy rather than a false "deleted" claim.
    return (
      <div className="writing-detail-page">
        <button type="button" className="writing-detail-back-btn" onClick={() => navigate('/writing/history')}>
          ← กลับ
        </button>
        <div className="writing-detail-status">{loadErrorMessage(queryError)}</div>
      </div>
    )
  }

  const deleteControls = confirmingDelete ? (
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
  )

  // Corrected: this page IS the ผลตรวจ (ADR-177). The raw text is not shown
  // again — block 1's marked text IS that text — and there is no edit button,
  // because a correction locks the text anyway (ADR-169).
  if (entry.correction) {
    return (
      <div className="writing-detail-page">
        <button type="button" className="writing-detail-back-btn" onClick={() => navigate('/writing/history')}>
          ← กลับ
        </button>
        <h1 className="writing-detail-result-title">ผลตรวจ · {formatDateThai(entry.date)}</h1>
        <CorrectionResult
          correction={entry.correction}
          wordsPerMinute={entry.wordsPerMinute}
          elapsedSeconds={entry.elapsedSeconds}
        />
        {error && <div className="writing-detail-error">{error}</div>}
        <div className="writing-detail-actions">{deleteControls}</div>
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
        <span className="writing-history-badge writing-history-badge--pending">⏳ รอตรวจ</span>
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
        {isEditing ? (
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
        {deleteControls}
      </div>
    </div>
  )
}
