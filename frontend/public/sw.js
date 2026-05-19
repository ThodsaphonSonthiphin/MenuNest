/**
 * MenuNest Service Worker — Phase 1 (migraine tracker follow-up pings).
 *
 * Responsibilities:
 *   1. Receive `push` events from the backend (web-push protocol via
 *      VAPID), display a notification with up-to-4 quick-response
 *      actions.
 *   2. On `notificationclick`:
 *      - For "resolved" / "same" — POST the answer directly without
 *        opening the app (0-tap response, the whole UX point of the
 *        feature).
 *      - For "improved" / "worse" or a plain click — focus an open
 *        /health/* tab or open one to the active episode page with the
 *        action pre-filled.
 *   3. Re-claim / skip-waiting so updates roll out cleanly.
 *   4. Handle `pushsubscriptionchange` defensively (browser revoked our
 *      subscription) — the page itself re-subscribes on next load
 *      because we need the VAPID key + credentialed API.
 *
 * Notes:
 *   - This file is served from /sw.js (Vite copies everything in
 *     `public/` to the dist root).
 *   - No caching strategy yet — Phase 1 is push-only, offline
 *     navigation is out of scope.
 *   - Payload shape mirrors what `WebPushNotificationService` on the
 *     backend serializes:
 *       { title, body, data: { pingId, episodeId }, actions: [...] }
 */

self.addEventListener('install', (event) => {
  // Take control on the next reload without waiting for old tabs.
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim())
})

self.addEventListener('push', (event) => {
  if (!event.data) return

  let payload
  try {
    payload = event.data.json()
  } catch (_err) {
    payload = { title: 'MenuNest', body: event.data.text() }
  }

  const data = payload.data || {}
  const options = {
    body: payload.body,
    icon: '/icons/icon-192.png',
    badge: '/icons/badge-72.png',
    data,
    actions: payload.actions || [],
    requireInteraction: false,
    // `tag` collapses duplicate pings for the same episode so the user
    // doesn't get a stack of identical alerts if the SW reactivates
    // mid-delivery.
    tag: data.pingId || 'menunest-default',
  }

  event.waitUntil(self.registration.showNotification(payload.title, options))
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()

  const data = event.notification.data || {}
  const { pingId, episodeId } = data
  const action = event.action

  if (!pingId) {
    // No follow-up context — just open the home screen.
    event.waitUntil(self.clients.openWindow('/health'))
    return
  }

  // ----- 0-tap quick responses ---------------------------------------
  // "resolved" and "same" don't need any further input from the user,
  // so we POST the answer straight from the SW and acknowledge with a
  // silent notification.
  if (action === 'resolved' || action === 'same') {
    const response = action === 'resolved' ? 'Resolved' : 'Same'
    event.waitUntil(
      fetch(`/api/followups/${pingId}/respond`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ response }),
        credentials: 'include',
      })
        .then(() =>
          self.registration.showNotification('✓ บันทึกคำตอบแล้ว', {
            tag: 'menunest-ack-' + pingId,
            silent: true,
            requireInteraction: false,
          }),
        )
        .catch((err) => {
          // Keep the failure visible so the user knows to open the app
          // and respond manually.
          console.warn('[sw] respond POST failed', err)
        }),
    )
    return
  }

  // ----- Open / focus the app for "improved" / "worse" / default ----
  const targetUrl = action
    ? `/health/active/${episodeId}?ping=${pingId}&action=${action}`
    : `/health/active/${episodeId}`

  event.waitUntil(
    self.clients
      .matchAll({ type: 'window', includeUncontrolled: true })
      .then((windowClients) => {
        for (const client of windowClients) {
          if (client.url.includes('/health/') && 'focus' in client) {
            client.focus()
            if ('navigate' in client) {
              try {
                client.navigate(targetUrl)
              } catch (_err) {
                /* navigate() can throw if cross-origin etc — fall back to openWindow */
              }
            }
            return
          }
        }
        return self.clients.openWindow(targetUrl)
      }),
  )
})

self.addEventListener('pushsubscriptionchange', (event) => {
  // The browser has revoked our push subscription. We can't fetch the
  // VAPID key from the SW context (it requires auth + the RTK API
  // layer), so the page is responsible for re-subscribing the next
  // time the user opens the app. Log so it's visible in DevTools.
  console.warn('[sw] pushsubscriptionchange — page must re-subscribe')
})

// ===== Pomodoro =====================================================
// The page schedules a notification via postMessage when the user
// starts/resumes a Pomodoro cycle. We hold the timeout id in-memory in
// the worker; if the worker is evicted before the timeout fires, the
// notification is lost — that's the documented iOS caveat and the page
// reconciles state from `startedAt` on next visit.
const pomodoroTimers = new Map()

const cancelPomodoroTimer = (id) => {
  const handle = pomodoroTimers.get(id)
  if (handle != null) {
    clearTimeout(handle)
    pomodoroTimers.delete(id)
  }
}

self.addEventListener('message', (event) => {
  const data = event.data
  if (!data || typeof data !== 'object') return

  if (data.type === 'POMODORO_SCHEDULE') {
    const { id, fireAt, title, body } = data.payload || {}
    if (!id || typeof fireAt !== 'number') return
    cancelPomodoroTimer(id)
    const delay = Math.max(0, fireAt - Date.now())
    const handle = setTimeout(() => {
      pomodoroTimers.delete(id)
      self.registration.showNotification(title || 'Pomodoro', {
        body: body || '',
        tag: 'menunest-pomodoro',
        icon: '/icons/icon-192.png',
        badge: '/icons/badge-72.png',
      })
    }, delay)
    pomodoroTimers.set(id, handle)
    return
  }

  if (data.type === 'POMODORO_CANCEL') {
    cancelPomodoroTimer(data.id)
    return
  }
})
