import { useRef, useState } from 'react'
import { usePhotoUpload } from '../hooks/usePhotoUpload'
import type {
  AttachedPhotoInfo,
  PhotoContainerKey,
  PhotoRefDto,
} from '../../../shared/api/healthTypes'

/**
 * Multi-photo upload with client-side compression + SAS-based direct
 * Azure Blob upload. Designed to be embedded inside a form; the parent
 * owns the "attach" mutation (drug or episode) and receives the
 * uploaded `AttachedPhotoInfo[]` via `onUploaded`.
 *
 * Mock: docs/mocks/patient-search-photo-mock.html (right phone — Photo
 * upload section). Empty / uploading / uploaded states all rendered.
 *
 *  - "📷 ถ่ายเลย" sets `capture="environment"` so mobile browsers open
 *    the camera directly. The fallback "🖼 Gallery" button uses the
 *    same input without `capture` so the OS file picker opens.
 *  - Compression happens in `usePhotoUpload`; this component is mostly
 *    presentation + invoking the hook.
 */
export interface PhotoUploaderProps {
  parentType: PhotoContainerKey
  parentId: string
  existing: PhotoRefDto[]
  onUploaded: (newPhotos: AttachedPhotoInfo[]) => void | Promise<void>
  onRemoveExisting?: (photoId: string) => void
  disabled?: boolean
}

export function PhotoUploader(props: PhotoUploaderProps) {
  const {
    parentType,
    parentId,
    existing,
    onUploaded,
    onRemoveExisting,
    disabled,
  } = props
  const cameraInputRef = useRef<HTMLInputElement>(null)
  const galleryInputRef = useRef<HTMLInputElement>(null)
  const { uploadFiles, isUploading, progress, error } = usePhotoUpload({
    parentType,
    parentId,
  })
  const [pendingCount, setPendingCount] = useState(0)

  const triggerCamera = () => cameraInputRef.current?.click()
  const triggerGallery = () => galleryInputRef.current?.click()

  const handleFiles = async (files: FileList | null) => {
    if (!files || files.length === 0) return
    const arr = Array.from(files)
    setPendingCount(arr.length)
    try {
      const uploaded = await uploadFiles(arr)
      if (uploaded.length > 0) await onUploaded(uploaded)
    } catch {
      /* error surfaced via the hook's `error` state */
    } finally {
      setPendingCount(0)
      // Reset both inputs so re-selecting the same file fires onchange.
      if (cameraInputRef.current) cameraInputRef.current.value = ''
      if (galleryInputRef.current) galleryInputRef.current.value = ''
    }
  }

  const isEmpty = existing.length === 0 && !isUploading

  return (
    <div className="health-photo-uploader">
      {/* Hidden inputs */}
      <input
        ref={cameraInputRef}
        type="file"
        accept="image/*"
        capture="environment"
        multiple
        style={{ display: 'none' }}
        onChange={(e) => handleFiles(e.target.files)}
      />
      <input
        ref={galleryInputRef}
        type="file"
        accept="image/*"
        multiple
        style={{ display: 'none' }}
        onChange={(e) => handleFiles(e.target.files)}
      />

      {/* Empty state */}
      {isEmpty && (
        <div className="health-photo-upload-area">
          <div className="health-photo-upload-icon">📷</div>
          <div className="health-photo-upload-text">เพิ่มรูปซองยา</div>
          <div className="health-photo-upload-sub">
            ผู้สูงอายุดูภาพได้ ไม่ต้องจำชื่อ
          </div>
          <div className="health-photo-upload-actions">
            <button
              type="button"
              className="health-photo-upload-btn"
              onClick={triggerCamera}
              disabled={disabled}
            >
              📷 ถ่ายเลย
            </button>
            <button
              type="button"
              className="health-photo-upload-btn health-photo-upload-btn--secondary"
              onClick={triggerGallery}
              disabled={disabled}
            >
              🖼 จาก gallery
            </button>
          </div>
        </div>
      )}

      {/* Existing thumbnails */}
      {existing.length > 0 && (
        <div className="health-photo-grid">
          {existing.map((p) => (
            <div key={p.id} className="health-photo-thumb-card">
              <img
                src={p.url}
                alt="drug photo"
                className="health-photo-thumb-img"
              />
              <div className="health-photo-thumb-actions">
                <a
                  href={p.url}
                  target="_blank"
                  rel="noreferrer"
                  className="health-photo-thumb-action"
                  aria-label="view"
                >
                  👁
                </a>
                {onRemoveExisting && (
                  <button
                    type="button"
                    className="health-photo-thumb-action health-photo-thumb-action--danger"
                    onClick={() => onRemoveExisting(p.id)}
                    aria-label="remove"
                  >
                    🗑
                  </button>
                )}
              </div>
            </div>
          ))}
          {!isUploading && (
            <button
              type="button"
              className="health-photo-thumb-card health-photo-thumb-card--add"
              onClick={triggerGallery}
              disabled={disabled}
              aria-label="add more"
            >
              + เพิ่ม
            </button>
          )}
        </div>
      )}

      {/* Uploading state */}
      {isUploading && (
        <div className="health-uploaded-photo-card">
          <div className="health-uploaded-photo-thumb">
            <div className="health-uploaded-photo-emoji">💊</div>
          </div>
          <div className="health-uploaded-photo-info">
            <div className="health-uploaded-photo-name">
              Uploading {pendingCount} photo
              {pendingCount > 1 ? 's' : ''}...
            </div>
            <div className="health-uploaded-photo-meta">
              upload ไป Azure Blob ({progress}%)
            </div>
            <div className="health-upload-progress">
              <div
                className="health-upload-progress-fill"
                style={{ width: `${progress}%` }}
              />
            </div>
          </div>
        </div>
      )}

      {error && (
        <div
          style={{
            marginTop: 8,
            padding: 10,
            borderRadius: 8,
            background: 'var(--hl-danger-bg)',
            color: 'var(--hl-danger)',
            fontSize: 12,
          }}
        >
          ⚠ {error}
        </div>
      )}
    </div>
  )
}
