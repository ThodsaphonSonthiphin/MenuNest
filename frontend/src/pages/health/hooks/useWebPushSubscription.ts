import { useCallback, useEffect, useState } from 'react'
import {
  useGetVapidPublicKeyQuery,
  useSubscribeWebPushMutation,
  useUnsubscribeWebPushMutation,
} from '../../../shared/api/api'

/**
 * Browser web-push subscription state + actions.
 *
 *  - `isSupported` short-circuits the UI when the browser doesn't ship
 *    Service Worker + PushManager (e.g., iOS Safari without PWA mode).
 *  - On mount we ask `navigator.serviceWorker.ready` if there is
 *    already a subscription, so the toggle reflects the real state.
 *  - The actual Service Worker registration is Task 15. For now this
 *    hook assumes `navigator.serviceWorker.ready` resolves after the
 *    SW lands; until then the subscribe path will error out cleanly
 *    (caught by the page and surfaced as a toast).
 *
 * VAPID key is base64-url; convert to a Uint8Array per the standard.
 */
export type WebPushPermission = NotificationPermission | 'unsupported'

export interface UseWebPushResult {
  isSupported: boolean
  isSubscribed: boolean
  isLoading: boolean
  error: string | null
  /** Reflects `Notification.permission` ('default' | 'granted' | 'denied'),
   *  or `'unsupported'` when the browser lacks the Notification API. */
  permission: WebPushPermission
  subscribe: () => Promise<void>
  unsubscribe: () => Promise<void>
}

function urlBase64ToUint8Array(base64String: string): ArrayBuffer {
  // The PushManager.subscribe API requires `BufferSource`; modern lib.dom
  // typings narrow this to `ArrayBufferView<ArrayBuffer>`, and a vanilla
  // `Uint8Array<ArrayBufferLike>` from `new Uint8Array(n)` no longer
  // satisfies that. Returning the underlying ArrayBuffer directly side-
  // steps the variance issue while still encoding the bytes correctly.
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4)
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/')
  const rawData = window.atob(base64)
  const buffer = new ArrayBuffer(rawData.length)
  const view = new Uint8Array(buffer)
  for (let i = 0; i < rawData.length; ++i) view[i] = rawData.charCodeAt(i)
  return buffer
}

function arrayBufferToBase64(buffer: ArrayBuffer | null): string {
  if (!buffer) return ''
  const bytes = new Uint8Array(buffer)
  let binary = ''
  for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i])
  return window.btoa(binary)
}

function readNotificationPermission(): WebPushPermission {
  if (typeof window === 'undefined' || typeof Notification === 'undefined') {
    return 'unsupported'
  }
  return Notification.permission
}

export function useWebPushSubscription(): UseWebPushResult {
  const isSupported =
    typeof window !== 'undefined' &&
    'serviceWorker' in navigator &&
    'PushManager' in window &&
    typeof Notification !== 'undefined'
  const [isSubscribed, setIsSubscribed] = useState(false)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [permission, setPermission] = useState<WebPushPermission>(
    readNotificationPermission,
  )

  const vapidQuery = useGetVapidPublicKeyQuery(undefined, { skip: !isSupported })
  const [subscribeMutation] = useSubscribeWebPushMutation()
  const [unsubscribeMutation] = useUnsubscribeWebPushMutation()

  // Reflect the real OS-level subscription state on mount, and keep
  // the permission flag in sync with the browser. We re-check on
  // visibility change because the user can flip permission in browser
  // settings while the tab is backgrounded.
  useEffect(() => {
    if (!isSupported) return
    let cancelled = false
    ;(async () => {
      try {
        const reg = await navigator.serviceWorker.ready
        const existing = await reg.pushManager.getSubscription()
        if (!cancelled) setIsSubscribed(!!existing)
      } catch {
        /* SW not yet active — first paint races with registration. */
      }
    })()
    const onVisibility = () => setPermission(readNotificationPermission())
    document.addEventListener('visibilitychange', onVisibility)
    return () => {
      cancelled = true
      document.removeEventListener('visibilitychange', onVisibility)
    }
  }, [isSupported])

  const subscribe = useCallback(async (): Promise<void> => {
    if (!isSupported) {
      setError('Browser ของคุณไม่รองรับ web push')
      return
    }
    setIsLoading(true)
    setError(null)
    try {
      const granted = await Notification.requestPermission()
      setPermission(granted)
      if (granted !== 'granted') {
        setError('การแจ้งเตือนไม่ได้รับอนุญาต')
        return
      }
      const publicKey = vapidQuery.data?.publicKey
      if (!publicKey) {
        setError('โหลด VAPID public key ไม่สำเร็จ')
        return
      }
      const reg = await navigator.serviceWorker.ready
      const sub = await reg.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlBase64ToUint8Array(publicKey),
      })
      const json = sub.toJSON() as {
        endpoint?: string
        keys?: { p256dh?: string; auth?: string }
        expirationTime?: number | null
      }
      const endpoint = json.endpoint ?? sub.endpoint
      // Some browsers don't expose toJSON keys reliably; fall back to
      // reading the raw ArrayBuffers and base64-encoding manually.
      const p256dh =
        json.keys?.p256dh ?? arrayBufferToBase64(sub.getKey('p256dh'))
      const auth = json.keys?.auth ?? arrayBufferToBase64(sub.getKey('auth'))
      const expiresAt = sub.expirationTime
        ? new Date(sub.expirationTime).toISOString()
        : null
      await subscribeMutation({ endpoint, p256dh, auth, expiresAt }).unwrap()
      setIsSubscribed(true)
    } catch (err) {
      const msg =
        err && typeof err === 'object' && 'message' in err
          ? String((err as { message?: unknown }).message)
          : 'subscribe ไม่สำเร็จ'
      setError(msg)
    } finally {
      setIsLoading(false)
    }
  }, [isSupported, vapidQuery.data, subscribeMutation])

  const unsubscribe = useCallback(async (): Promise<void> => {
    if (!isSupported) return
    setIsLoading(true)
    setError(null)
    try {
      const reg = await navigator.serviceWorker.ready
      const sub = await reg.pushManager.getSubscription()
      if (sub) {
        await unsubscribeMutation({ endpoint: sub.endpoint }).unwrap()
        await sub.unsubscribe()
      }
      setIsSubscribed(false)
    } catch (err) {
      const msg =
        err && typeof err === 'object' && 'message' in err
          ? String((err as { message?: unknown }).message)
          : 'unsubscribe ไม่สำเร็จ'
      setError(msg)
    } finally {
      setIsLoading(false)
    }
  }, [isSupported, unsubscribeMutation])

  return {
    isSupported,
    isSubscribed,
    isLoading,
    error,
    permission,
    subscribe,
    unsubscribe,
  }
}
