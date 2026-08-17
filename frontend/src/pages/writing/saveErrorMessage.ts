/**
 * Turns a failed `PUT /api/writing-entries/{id}` into the message the writer
 * sees (WMCP-26).
 *
 * Why this reads the *response* and not the page's cached `correctedAt`: a
 * correction arrives over MCP, and the detail page only learns about it on its
 * 15s poll. So a correction can land while the PUT is in flight -- the client
 * still believes the entry is unlocked, and the server refuses anyway. The 400
 * is the only signal that is never stale.
 *
 * `ExceptionHandlingMiddleware` maps `DomainException` to HTTP 400 with
 * `ProblemDetails.Detail` set to the domain message verbatim, so matching on
 * that string identifies the lock refusal specifically -- a validation failure
 * or a network error still gets the generic retry message, which for those is
 * honest advice.
 */
export const LOCKED_SAVE_MESSAGE = 'คืนนี้ถูกตรวจแล้ว — แก้ข้อความไม่ได้'
export const GENERIC_SAVE_MESSAGE = 'บันทึกไม่สำเร็จ ลองอีกครั้ง'

/** The message `WritingEntry.UpdateText` throws once a correction exists. */
const LOCK_DETAIL = 'Cannot edit text after a correction has been recorded.'

export function saveErrorMessage(err: unknown): string {
  if (typeof err !== 'object' || err === null) return GENERIC_SAVE_MESSAGE

  const data = (err as { data?: unknown }).data
  if (typeof data !== 'object' || data === null) return GENERIC_SAVE_MESSAGE

  const detail = (data as { detail?: unknown }).detail
  if (typeof detail !== 'string') return GENERIC_SAVE_MESSAGE

  return detail.includes(LOCK_DETAIL) ? LOCKED_SAVE_MESSAGE : GENERIC_SAVE_MESSAGE
}
