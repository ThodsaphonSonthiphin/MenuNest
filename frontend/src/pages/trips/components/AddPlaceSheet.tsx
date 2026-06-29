// frontend/src/pages/trips/components/AddPlaceSheet.tsx
import {useState} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {TextBox} from '@syncfusion/react-inputs'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {
  useResolvePlaceMutation,
  useAddTripPlaceMutation,
  type ResolvedPlaceDto,
  type PlaceCategory,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

const CATS: {label: string; value: PlaceCategory}[] = [
  {label: '⛩️ ที่เที่ยว', value: 'See'},
  {label: '🍜 ร้านอาหาร', value: 'Eat'},
  {label: '☕ คาเฟ่', value: 'Cafe'},
  {label: '🛏️ ที่พัก', value: 'Stay'},
  {label: '🛍️ ช้อปปิ้ง', value: 'Shop'},
  {label: '📍 อื่นๆ', value: 'Other'},
]

export function AddPlaceSheet({tripId, onClose}: {tripId: string; onClose: () => void}) {
  const [url, setUrl] = useState('')
  const [resolved, setResolved] = useState<ResolvedPlaceDto | null>(null)
  const [category, setCategory] = useState<PlaceCategory>('See')

  const [resolvePlace, {isLoading: resolving, error: resolveError}] = useResolvePlaceMutation()
  const [addPlace, {isLoading: saving, error: saveError}] = useAddTripPlaceMutation()

  const doResolve = async () => {
    try {
      const r = await resolvePlace({url}).unwrap()
      setResolved(r)
      setCategory(r.category)
    } catch {
      // error surfaced via resolveError
    }
  }

  const doSave = async () => {
    if (!resolved) return
    try {
      await addPlace({
        tripId,
        googlePlaceId: resolved.googlePlaceId,
        name: resolved.name,
        lat: resolved.lat,
        lng: resolved.lng,
        address: resolved.address,
        category,
        priceLevel: resolved.priceLevel,
        photoUrl: resolved.photoUrl,
        openingHoursJson: resolved.openingHoursJson,
      }).unwrap()
      onClose()
    } catch {
      // error surfaced via saveError
    }
  }

  return (
    <Dialog
      open
      onClose={onClose}
      modal
      header="เพิ่มสถานที่จาก Google Maps"
      style={{width: '440px'}}
    >
      <div className="add-place-sheet">

        {/* ── Link input ── */}
        <div className="trip-form-field">
          <label className="trip-form-label">วางลิงก์จาก Google Maps</label>
          <div className="add-place-row">
            <TextBox
              value={url}
              onChange={(e: {value?: string}) => setUrl(e.value ?? '')}
              placeholder="https://maps.app.goo.gl/…"
            />
            <Button
              type="button"
              variant={Variant.Filled}
              color={Color.Primary}
              disabled={!url || resolving}
              onClick={doResolve}
            >
              {resolving ? 'กำลังดึง…' : 'ดึงข้อมูล'}
            </Button>
          </div>
          {resolveError && (
            <p className="trips-field-error">{getErrorMessage(resolveError)}</p>
          )}
        </div>

        {/* ── Resolved preview ── */}
        {resolved && (
          <div className="add-place-preview">
            <div className="place-name">{resolved.name}</div>
            <div className="place-coords">
              {resolved.lat.toFixed(5)}, {resolved.lng.toFixed(5)}
            </div>
            {resolved.address && (
              <div className="place-sub">{resolved.address}</div>
            )}

            <div className="trip-form-field" style={{marginTop: '12px'}}>
              <label className="trip-form-label">หมวดหมู่</label>
              <DropDownList
                dataSource={CATS}
                fields={{text: 'label', value: 'value'}}
                value={category}
                onChange={(e: {value: unknown}) =>
                  setCategory((e.value as PlaceCategory) ?? 'Other')
                }
              />
            </div>

            {saveError && (
              <p className="trips-field-error">{getErrorMessage(saveError)}</p>
            )}

            <div className="trip-form-actions">
              <Button
                type="button"
                variant={Variant.Outlined}
                color={Color.Secondary}
                onClick={onClose}
              >
                ยกเลิก
              </Button>
              <Button
                type="button"
                variant={Variant.Filled}
                color={Color.Primary}
                disabled={saving}
                onClick={doSave}
              >
                {saving ? 'กำลังบันทึก…' : 'บันทึกลงทริป'}
              </Button>
            </div>
          </div>
        )}

        {/* ── MVP hint ── */}
        <p className="add-place-hint">
          MVP รองรับเฉพาะวางลิงก์ — แชร์จากแอป / bookmarklet = Phase 2
        </p>
      </div>
    </Dialog>
  )
}
