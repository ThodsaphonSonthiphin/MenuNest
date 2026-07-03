// frontend/src/pages/trips/components/PlaceLinkFallbackDialog.tsx
// Hidden fallback (ADR-014): paste a Google Maps link → server-side resolve
// (SSRF-guarded) → hand the ResolvedPlaceDto up so AddPlaceMode shows the same
// preview as the search/tap paths. This is the surviving half of the old
// AddPlaceSheet; the search/tap paths are the primary entry now.
import {useState} from 'react'
import {Dialog} from '@syncfusion/react-popups'
import {TextBox} from '@syncfusion/react-inputs'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {useResolvePlaceMutation, type ResolvedPlaceDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

export interface PlaceLinkFallbackDialogProps {
  onResolved(dto: ResolvedPlaceDto): void
  onClose(): void
}

export function PlaceLinkFallbackDialog({onResolved, onClose}: PlaceLinkFallbackDialogProps) {
  const [url, setUrl] = useState('')
  const [resolvePlace, {isLoading, error}] = useResolvePlaceMutation()

  const doResolve = async () => {
    try {
      const dto = await resolvePlace({url}).unwrap()
      onResolved(dto)
      onClose()
    } catch { /* surfaced via error */ }
  }

  return (
    <Dialog open onClose={onClose} modal header="วางลิงก์จาก Google Maps" style={{width: '420px'}}>
      <div className="add-place-sheet">
        <div className="trip-form-field">
          <label className="trip-form-label">วางลิงก์จาก Google Maps</label>
          <div className="add-place-row">
            <TextBox
              value={url}
              onChange={(e: {value?: string}) => setUrl(e.value ?? '')}
              placeholder="https://maps.app.goo.gl/…"
            />
            <Button
              type="button" variant={Variant.Filled} color={Color.Primary}
              disabled={!url || isLoading} onClick={doResolve}
            >
              {isLoading ? 'กำลังดึง…' : 'ดึงข้อมูล'}
            </Button>
          </div>
          {error && <p className="trips-field-error">{getErrorMessage(error)}</p>}
        </div>
      </div>
    </Dialog>
  )
}
