import { describe, expect, it } from 'vitest'
import {
  GENERIC_SAVE_MESSAGE,
  LOCKED_SAVE_MESSAGE,
  saveErrorMessage,
} from './saveErrorMessage'

// The shape RTK Query's fetchBaseQuery hands back for a 400: the parsed
// ProblemDetails body under `data`. ExceptionHandlingMiddleware puts the domain
// message in `detail`.
const lockRefusal = {
  status: 400,
  data: {
    title: 'Request rejected',
    detail: 'Cannot edit text after a correction has been recorded.',
    status: 400,
  },
}

describe('saveErrorMessage', () => {
  it("names the lock instead of promising a retry when the server refuses a locked edit", () => {
    expect(saveErrorMessage(lockRefusal)).toBe(LOCKED_SAVE_MESSAGE)
  })

  it('never tells the writer to try again on the locked path', () => {
    expect(saveErrorMessage(lockRefusal)).not.toContain('ลองอีกครั้ง')
  })

  it('keeps the retry message for a validation failure, which a retry can fix', () => {
    const validationFailure = {
      status: 400,
      data: {
        title: 'One or more validation errors occurred.',
        errors: { Text: ['Text is required.'] },
        status: 400,
      },
    }
    expect(saveErrorMessage(validationFailure)).toBe(GENERIC_SAVE_MESSAGE)
  })

  it('keeps the retry message for a network error, which has no body at all', () => {
    expect(saveErrorMessage({ status: 'FETCH_ERROR', error: 'Failed to fetch' })).toBe(
      GENERIC_SAVE_MESSAGE,
    )
  })

  it('survives junk without throwing', () => {
    expect(saveErrorMessage(undefined)).toBe(GENERIC_SAVE_MESSAGE)
    expect(saveErrorMessage(null)).toBe(GENERIC_SAVE_MESSAGE)
    expect(saveErrorMessage('boom')).toBe(GENERIC_SAVE_MESSAGE)
    expect(saveErrorMessage({ data: { detail: 42 } })).toBe(GENERIC_SAVE_MESSAGE)
  })
})
