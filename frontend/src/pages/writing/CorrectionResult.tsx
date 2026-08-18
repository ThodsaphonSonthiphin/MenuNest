import type { ReactNode } from 'react'
import type { WritingCorrectionDto } from '../../shared/api/writingTypes'
import { sanitizeMarkedText } from './sanitizeMarkedText'
import './CorrectionResult.css'

interface Props {
  correction: WritingCorrectionDto
  wordsPerMinute: number
  elapsedSeconds: number
}

function Block({ n, title, children }: { n: number; title: string; children: ReactNode }) {
  return (
    <section className="correction-block">
      <p className="correction-block__head">
        <span className="correction-block__n">{n}</span>
        <span className="correction-block__title">{title}</span>
      </p>
      {children}
    </section>
  )
}

/**
 * The five fixed blocks of one night's Correction (ADR-178). All five render on
 * every corrected night, in this order: a block with no data states why it is
 * empty rather than disappearing, so the numbering is the same on every night
 * and "empty" is never mistaken for "the AI skipped it".
 */
export function CorrectionResult({ correction, wordsPerMinute, elapsedSeconds }: Props) {
  const {
    targetRule,
    markedText,
    hitCount,
    missCount,
    thaiWhyLine,
    sentenceCombiningItems,
    stuckWords,
    errorsPer100Words,
  } = correction

  const totalMarks = hitCount + missCount
  const wordCount = Math.round((wordsPerMinute * elapsedSeconds) / 60)

  return (
    <div className="correction-result">
      <Block n={1} title={`เป้าหมายตอนนี้ · ${targetRule}`}>
        {/* Sanitized above with a closed allow-list (ADR-180): p/span/br only,
            class restricted to miss|fix|hit|th. Never render markedText raw. */}
        <div
          className="correction-marked"
          dangerouslySetInnerHTML={{ __html: sanitizeMarkedText(markedText) }}
        />
        <p className="correction-tally">
          ต้องเติม {totalMarks} ที่ · ถูก {hitCount} · พลาด {missCount}
        </p>
        {totalMarks === 0 && <p className="correction-empty">คืนนี้ไม่มีจุดไหนเข้ากฎนี้</p>}
      </Block>

      <Block n={2} title="ทำไม (ภาษาไทย)">
        <div className="correction-why">{thaiWhyLine}</div>
      </Block>

      <Block n={3} title="ต่อประโยค (จากประโยคของคุณเอง)">
        {sentenceCombiningItems.length === 0 ? (
          <p className="correction-empty">คืนนี้ไม่มีประโยคอังกฤษให้ต่อ</p>
        ) : (
          <div className="correction-combine">
            {sentenceCombiningItems.map((item, i) => (
              <div key={i} className="correction-combine__item">
                <span className="correction-combine__src">{item.source}</span>
                <br />
                <span className="correction-combine__arrow">→</span> {item.combined}
              </div>
            ))}
          </div>
        )}
      </Block>

      <Block n={4} title="คำที่นึกไม่ออก (จาก [วงเล็บ])">
        {stuckWords.length === 0 ? (
          <p className="correction-empty">คืนนี้ไม่มีคำในวงเล็บ</p>
        ) : (
          <div className="correction-stuck">
            {stuckWords.map((word, i) => (
              <div key={i} className="correction-stuck__card">
                <div className="correction-stuck__thai">{word.thai}</div>
                <div className="correction-stuck__english">{word.english}</div>
              </div>
            ))}
          </div>
        )}
      </Block>

      <Block n={5} title="ตัวเลขวันนี้">
        <div className="correction-nums">
          <div className="correction-num">
            <div className="correction-num__key">คำ/นาที</div>
            <div className="correction-num__value">{wordsPerMinute.toFixed(1)}</div>
            <div className="correction-num__note">
              {wordCount} คำ ใน {elapsedSeconds} วินาที
            </div>
          </div>
          <div className="correction-num">
            <div className="correction-num__key">พลาด/100 คำ</div>
            <div className="correction-num__value">{errorsPer100Words.toFixed(1)}</div>
            <div className="correction-num__note">เฉพาะกฎ {targetRule} เท่านั้น</div>
          </div>
        </div>
      </Block>

      <div className="correction-never">
        <b>สิ่งที่ระบบจะไม่ทำเด็ดขาด</b> — ไม่เขียนข้อความของคุณใหม่ · ไม่ให้คะแนน ไม่ชม ·
        ไม่แก้ที่ผิดข้ออื่น · ไม่ให้แก้ข้อความเดิมแล้วตรวจซ้ำ
      </div>
    </div>
  )
}
