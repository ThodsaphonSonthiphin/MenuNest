import {ReviewIcon} from './ReviewIcon'
import {MAX_REVIEW_LINKS, type ReviewDraft} from '../lib/reviewLinks'

export function ReviewLinksSection({
  drafts,
  onChange,
}: {
  drafts: ReviewDraft[]
  onChange: (drafts: ReviewDraft[]) => void
}) {
  return (
    <section className="se-sec">
      <div className="se-sec-head">
        <ReviewIcon />ลิงก์รีวิว (TikTok ฯลฯ)
      </div>
      {drafts.map((d, i) => (
        <div className="rv-row" key={i}>
          <input
            className="rv-url"
            type="url"
            placeholder="https://www.tiktok.com/@..."
            value={d.url}
            onChange={(e) => onChange(drafts.map((r, j) => (j === i ? {...r, url: e.target.value} : r)))}
          />
          <input
            className="rv-lab"
            placeholder="ป้ายกำกับ (ไม่บังคับ)"
            value={d.label}
            onChange={(e) => onChange(drafts.map((r, j) => (j === i ? {...r, label: e.target.value} : r)))}
          />
          <button
            type="button"
            className="rv-del"
            aria-label="ลบลิงก์"
            onClick={() => onChange(drafts.filter((_, j) => j !== i))}
          >
            ✕
          </button>
        </div>
      ))}
      {drafts.length < MAX_REVIEW_LINKS && (
        <button
          type="button"
          className="rv-add"
          onClick={() => onChange([...drafts, {url: '', label: ''}])}
        >
          + เพิ่มลิงก์รีวิว
        </button>
      )}
    </section>
  )
}