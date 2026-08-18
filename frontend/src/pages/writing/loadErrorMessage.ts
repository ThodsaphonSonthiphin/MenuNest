/**
 * Turns a failed `GET /api/writing-entries/{id}` into the message shown on
 * `WritingEntryDetailPage` when there is no entry to render (fix round 1,
 * #97).
 *
 * Why 404 is NOT treated as "the entry is gone": `ExceptionHandlingMiddleware`
 * maps every `DomainException` -- including the one `GetWritingEntryHandler`
 * throws for a missing, deleted, or foreign entry -- to HTTP **400**, never
 * 404. There is no code path in this API that produces a literal 404 for a
 * missing entry; the only way this SPA sees one is a route that does not
 * exist yet (e.g. the frontend deployed ahead of the backend). That is a
 * transient condition a retry genuinely fixes, so a 404 gets the generic
 * "loading failed" copy, not the "this night may have been deleted" one.
 *
 * Why this reads the *response* and not some cached page state: it is the
 * only signal available here at all -- there is no prior successful fetch of
 * this entry on this page to compare against. `ExceptionHandlingMiddleware`
 * puts the domain message in `ProblemDetails.Detail` verbatim, so matching on
 * that string identifies "not found" specifically -- everything else
 * (validation failure, network error, an actual 404) is honestly a retry-able
 * failure rather than a deletion.
 */
export const NOT_FOUND_MESSAGE = 'ไม่พบรายการนี้ (อาจถูกลบไปแล้ว)'
export const GENERIC_LOAD_MESSAGE = 'โหลดไม่สำเร็จ'

/** The message `GetWritingEntryHandler` throws for a missing, deleted, or foreign entry. */
const NOT_FOUND_DETAIL = 'Writing entry not found.'

export function loadErrorMessage(queryError: unknown): string {
  // No error at all: the page has nothing to show and no reason offered why
  // -- treated the same as before this fix (RTK Query's initial/settled state
  // with no data and no error reads as "gone").
  if (!queryError) return NOT_FOUND_MESSAGE

  if (typeof queryError !== 'object') return GENERIC_LOAD_MESSAGE

  const status = (queryError as { status?: unknown }).status
  if (status !== 400) return GENERIC_LOAD_MESSAGE

  const data = (queryError as { data?: unknown }).data
  if (typeof data !== 'object' || data === null) return GENERIC_LOAD_MESSAGE

  const detail = (data as { detail?: unknown }).detail
  if (typeof detail !== 'string') return GENERIC_LOAD_MESSAGE

  return detail.includes(NOT_FOUND_DETAIL) ? NOT_FOUND_MESSAGE : GENERIC_LOAD_MESSAGE
}
