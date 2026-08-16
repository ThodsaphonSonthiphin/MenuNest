import { useRef, useState } from 'react'
import {
  RichTextEditorComponent,
  Inject,
  Toolbar,
  Link,
  HtmlEditor,
  QuickToolbar,
  type RichTextEditorComponent as RteInstance,
} from '@syncfusion/ej2-react-richtexteditor'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { useWritingTimer } from './useWritingTimer'
import { useSubmitWritingEntryMutation } from '../../shared/api/api'
import './WritingPage.css'

const formatMMSS = (ms: number): string => {
  const totalSec = Math.ceil(ms / 1000)
  const m = Math.floor(totalSec / 60)
  const s = totalSec % 60
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

const todayKey = (): string => {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

export function WritingPage() {
  const { remainingMs, isDone, startedAtMs } = useWritingTimer()
  const [submitWritingEntry, { isLoading, isSuccess }] = useSubmitWritingEntryMutation()
  const rteRef = useRef<RteInstance | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const handleSubmit = async () => {
    const html = rteRef.current?.getHtml() ?? ''
    const elapsedSeconds = Math.min(3600, Math.round((Date.now() - startedAtMs) / 1000))
    setSubmitError(null)
    try {
      await submitWritingEntry({
        date: todayKey(),
        text: html,
        elapsedSeconds,
      }).unwrap()
    } catch (err) {
      console.error('submitWritingEntry failed', err)
      setSubmitError('ส่งไม่สำเร็จ ลองอีกครั้ง')
    }
  }

  return (
    <div className="writing-page" data-testid="writing-page">
      <div className="writing-timer" data-testid="writing-timer">
        {formatMMSS(remainingMs)}
      </div>
      <div className="writing-timer-note">นับถอยหลังจาก 7:00 · เดินต่อแม้ล็อกหน้าจอ</div>

      <RichTextEditorComponent
        ref={rteRef}
        height={300}
        placeholder="เขียนถึงครอบครัววันนี้เป็นภาษาอังกฤษ..."
      >
        <Inject services={[Toolbar, Link, HtmlEditor, QuickToolbar]} />
      </RichTextEditorComponent>

      {isSuccess ? (
        <div className="writing-done-badge" data-testid="writing-done-badge">
          ✓ วันนี้เสร็จแล้ว
        </div>
      ) : (
        <Button
          variant={Variant.Standard}
          color={Color.Primary}
          onClick={handleSubmit}
          disabled={isLoading}
          data-testid="writing-submit"
        >
          {isDone ? 'ส่ง' : 'ส่งก่อนครบเวลา'}
        </Button>
      )}
      {submitError && <div className="writing-error">{submitError}</div>}
      <div className="writing-correction-note">แก้ทีหลังได้ ผ่าน Claude Code เมื่อไหร่ก็ได้</div>
    </div>
  )
}
