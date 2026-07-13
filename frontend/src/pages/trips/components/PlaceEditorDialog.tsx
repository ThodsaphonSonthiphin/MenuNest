import {useState} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {
  useUpdateTripPlaceMutation,
  useDeleteTripPlaceMutation,
  usePushPlaceProfileMutation,
  type TripPlaceDto,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {catColor, catLabel} from '../placeCategory'
import {BestTimeBar} from './BestTimeBar'
import {ReviewLinksSection} from './ReviewLinksSection'
import {ChecklistSection} from './ChecklistSection'
import {sanitizeReviewDrafts, draftsValid, MAX_REVIEW_LINKS, type ReviewDraft} from '../lib/reviewLinks'

export function PlaceEditorDialog({
  tripId,
  place,
  onClose,
}: {
  tripId: string
  place: TripPlaceDto
  onClose: () => void
}) {
  const [bestStart, setBestStart] = useState<string | null>(place.bestTimeStart ?? null)
  const [bestEnd, setBestEnd] = useState<string | null>(place.bestTimeEnd ?? null)
  const [reviewDrafts, setReviewDrafts] = useState<ReviewDraft[]>(
    (place.reviewLinks ?? []).map((l) => ({url: l.url, label: l.label ?? ''})),
  )
  const [saveError, setSaveError] = useState<string | null>(null)
  const [pushed, setPushed] = useState(false)

  const [updatePlace, {isLoading: saving}] = useUpdateTripPlaceMutation()
  const [deletePlace] = useDeleteTripPlaceMutation()
  const [pushProfile, {isLoading: pushing}] = usePushPlaceProfileMutation()

  const persist = async () => {
    await updatePlace({
      tripId,
      placeId: place.id,
      name: place.name,
      category: place.category,
      address: place.address,
      feeNote: place.feeNote,
      notes: place.notes,
      bestTimeStart: bestStart,
      bestTimeEnd: bestEnd,
      reviewLinks: sanitizeReviewDrafts(reviewDrafts),
    }).unwrap()
  }

  const save = async () => {
    setSaveError(null)
    if (!draftsValid(reviewDrafts)) {
      setSaveError(`ลิงก์รีวิวไม่ถูกต้อง หรือเกิน ${MAX_REVIEW_LINKS} ลิงก์`)
      return
    }
    try {
      await persist()
      onClose()
    } catch (err) {
      setSaveError(getErrorMessage(err))
    }
  }

  const handleDelete = async () => {
    setSaveError(null)
    try {
      await deletePlace({tripId, placeId: place.id}).unwrap()
      onClose()
    } catch (err) {
      setSaveError(getErrorMessage(err))
    }
  }

  const handlePush = async () => {
    setSaveError(null)
    if (!draftsValid(reviewDrafts)) {
      setSaveError(`ลิงก์รีวิวไม่ถูกต้อง หรือเกิน ${MAX_REVIEW_LINKS} ลิงก์`)
      return
    }
    try {
      // Save on-screen values first. If no master exists yet, this Save auto-creates it
      // from those values (ADR-064) — so a separate push is only needed to OVERWRITE an
      // existing master, avoiding a redundant second write on the first-time path.
      await persist()
      if (place.hasProfile) await pushProfile({tripId, placeId: place.id}).unwrap()
      setPushed(true)
    } catch (err) {
      setSaveError(getErrorMessage(err))
    }
  }

  const header = (
    <div className="se-head">
      <div className="se-title">{place.name}</div>
      <div className="se-meta">
        <span className="se-cat">
          <span className="se-cat-dot" style={{background: catColor(place.category)}} />
          {catLabel(place.category)}
        </span>
        <span className="se-crumb">
          <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h10" /></svg>
          คลังสถานที่
        </span>
      </div>
    </div>
  )

  return (
    <Dialog
      open
      onClose={onClose}
      modal
      className="stop-editor-dialog"
      header={header}
      style={{width: 'min(480px, calc(100vw - 24px))'}}
    >
      <div className="stop-editor">
        {place.hasProfile && (
          <div className="se-seed-hint">
            <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M20 6L9 17l-5-5" /></svg>
            เติมจากคลังของคุณ — แก้ในทริปนี้ได้เลย ไม่กระทบทริปอื่น
          </div>
        )}

        <BestTimeBar start={bestStart} end={bestEnd} onChange={(s, e) => { setBestStart(s); setBestEnd(e); setPushed(false) }} />

        <ReviewLinksSection drafts={reviewDrafts} onChange={(d) => { setReviewDrafts(d); setPushed(false) }} />

        <ChecklistSection tripId={tripId} placeId={place.id} checklist={place.checklist ?? []} />

        {saveError && <p className="trips-field-error">{saveError}</p>}

        <div className="se-foot">
          <button type="button" className="se-delete" onClick={handleDelete}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14" /></svg>
            เอาออกจากทริปนี้
          </button>
          <div className="se-actions">
            {pushed && <span className="se-pushed">✓ อัปเดตคลังแล้ว</span>}
            {place.googlePlaceId && (
              <button type="button" className="se-push" disabled={saving || pushing} onClick={handlePush}>
                <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 19V5M5 12l7-7 7 7" /></svg>
                ดันขึ้น master
              </button>
            )}
            <button type="button" className="se-save" disabled={saving || pushing} onClick={save}>บันทึก</button>
          </div>
        </div>
      </div>
    </Dialog>
  )
}