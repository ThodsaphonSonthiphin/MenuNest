import { useState } from 'react'
import type { TripPlaceDto } from '../../../shared/api/api'
import type { ProximityStatus } from '../lib/proximity'

export interface LocationDisplayProps {
  place: TripPlaceDto
  distanceKm?: number | null
  status?: ProximityStatus
}

export function LocationDisplay({ place, distanceKm, status = 'Unknown' }: LocationDisplayProps) {
  const [copied, setCopied] = useState(false)
  
  // Use address if available, fallback to name
  const address = place.address || place.name
  const latLngStr = `${place.lat}, ${place.lng}`

  const handleCopy = async () => {
    const textToCopy = [address, latLngStr].join('\n')
    try {
      await navigator.clipboard.writeText(textToCopy)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch (e) {
      console.error('Failed to copy', e)
    }
  }

  return (
    <div className="location-display" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '12px 16px', margin: '8px 0', background: 'var(--bg-surface, #f9f9f9)', borderRadius: '8px' }}>
      <div className="location-text-group" style={{ display: 'flex', flexDirection: 'column', flex: 1, paddingRight: '16px', overflow: 'hidden' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
          <span className="location-address" style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
            {address}
          </span>
          {status === 'Arrived' && (
            <span className="status-badge" style={{ fontSize: '0.75rem', padding: '2px 6px', borderRadius: '4px', background: 'var(--color-success, #28a745)', color: '#fff', fontWeight: 'bold' }}>
              ถึงแล้ว (Arrived)
            </span>
          )}
          {status === 'Approaching' && (
            <span className="status-badge" style={{ fontSize: '0.75rem', padding: '2px 6px', borderRadius: '4px', background: 'var(--color-warning, #ffc107)', color: '#000', fontWeight: 'bold' }}>
              กำลังเข้าใกล้ (Approaching)
            </span>
          )}
        </div>
        <span className="location-latlng" style={{ fontSize: '0.85rem', color: 'var(--text-secondary)', marginTop: '4px' }}>
          {latLngStr} 
          {distanceKm != null && <span style={{ marginLeft: '8px', color: 'var(--text-tertiary)' }}>· ห่าง {distanceKm.toFixed(2)} km</span>}
        </span>
      </div>
      <button 
        type="button"
        className="location-copy-btn sd-act" 
        onClick={handleCopy} 
        title="Copy Location"
        style={{ width: '84px', display: 'flex', justifyContent: 'center', alignItems: 'center', border: '1px solid var(--border)', borderRadius: '4px', background: '#fff', cursor: 'pointer', padding: '8px' }}
      >
        {copied ? '✓' : 'คัดลอก'}
      </button>
    </div>
  )
}
