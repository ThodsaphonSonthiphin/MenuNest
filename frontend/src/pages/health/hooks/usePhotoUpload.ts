import { useCallback, useState } from 'react'
import { useRequestUploadSasMutation } from '../../../shared/api/api'
import type {
  AttachedPhotoInfo,
  PhotoContainerKey,
} from '../../../shared/api/healthTypes'

/**
 * Orchestrates the SAS-based photo upload flow used by `PhotoUploader`.
 *
 * The flow is intentionally three-step (compress → SAS → PUT) so the
 * client can keep the payload small and skip the application server
 * entirely for the actual blob bytes:
 *
 *  1. Compress each file to ≤ 1600px wide, JPEG q=0.85 (~< 500 KB).
 *  2. POST `/api/photos/upload-sas` to get a short-lived write URL.
 *  3. PUT the blob directly to Azure Blob Storage.
 *
 * Callers receive `AttachedPhotoInfo[]` so they can pass them to the
 * relevant `attach*Photos` mutation (drug or episode).
 *
 * Failures bubble up via the returned `error` state. We surface a
 * coarse 0–100 `progress` so the UI can render a progress bar across
 * the batch — per-file progress would require XHR + onprogress which
 * is more code than this Phase 1 worker needs.
 */
export interface UsePhotoUploadOptions {
  parentType: PhotoContainerKey
  parentId: string
}

export interface UsePhotoUploadResult {
  uploadFiles: (files: File[]) => Promise<AttachedPhotoInfo[]>
  progress: number
  isUploading: boolean
  error: string | null
}

const MAX_DIMENSION = 1600
const JPEG_QUALITY = 0.85

async function compressImage(file: File): Promise<Blob> {
  // SVG / non-rasterable types — skip compression and ship as-is.
  if (!file.type.startsWith('image/') || file.type === 'image/svg+xml') {
    return file
  }
  const dataUrl = await readFileAsDataUrl(file)
  const img = await loadImage(dataUrl)
  const { width, height } = scaleToMax(img.width, img.height, MAX_DIMENSION)

  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const ctx = canvas.getContext('2d')
  if (!ctx) return file
  ctx.drawImage(img, 0, 0, width, height)

  const blob = await new Promise<Blob | null>((resolve) =>
    canvas.toBlob(resolve, 'image/jpeg', JPEG_QUALITY),
  )
  // If the compressed JPEG actually grew (rare, but possible for tiny
  // PNGs of solid color), fall back to the raw file.
  if (!blob || blob.size >= file.size) return file
  return blob
}

function readFileAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onerror = () => reject(reader.error)
    reader.onload = () => resolve(String(reader.result))
    reader.readAsDataURL(file)
  })
}

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    img.onload = () => resolve(img)
    img.onerror = () => reject(new Error('Failed to decode image'))
    img.src = src
  })
}

function scaleToMax(
  w: number,
  h: number,
  max: number,
): { width: number; height: number } {
  if (w <= max && h <= max) return { width: w, height: h }
  const ratio = w >= h ? max / w : max / h
  return {
    width: Math.round(w * ratio),
    height: Math.round(h * ratio),
  }
}

export function usePhotoUpload({
  parentType,
  parentId,
}: UsePhotoUploadOptions): UsePhotoUploadResult {
  const [requestSas] = useRequestUploadSasMutation()
  const [progress, setProgress] = useState(0)
  const [isUploading, setIsUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const uploadFiles = useCallback(
    async (files: File[]): Promise<AttachedPhotoInfo[]> => {
      if (files.length === 0) return []
      setIsUploading(true)
      setProgress(0)
      setError(null)
      const uploaded: AttachedPhotoInfo[] = []
      try {
        for (let i = 0; i < files.length; i++) {
          const file = files[i]
          const blob = await compressImage(file)
          const contentType = blob.type || 'application/octet-stream'

          const sas = await requestSas({
            containerKey: parentType,
            parentId,
            contentType,
          }).unwrap()

          const putRes = await fetch(sas.uploadUrl, {
            method: 'PUT',
            body: blob,
            headers: {
              'x-ms-blob-type': 'BlockBlob',
              'Content-Type': contentType,
            },
          })
          if (!putRes.ok) {
            throw new Error(`Azure upload failed (${putRes.status})`)
          }

          uploaded.push({
            blobUrl: sas.blobUrl,
            fileSize: blob.size,
            contentType,
          })
          setProgress(Math.round(((i + 1) / files.length) * 100))
        }
        return uploaded
      } catch (err) {
        const msg =
          err && typeof err === 'object' && 'message' in err
            ? String((err as { message?: unknown }).message)
            : 'อัปโหลดรูปไม่สำเร็จ'
        setError(msg)
        throw err
      } finally {
        setIsUploading(false)
      }
    },
    [requestSas, parentType, parentId],
  )

  return { uploadFiles, progress, isUploading, error }
}
