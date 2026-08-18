import { describe, expect, it } from 'vitest'
import {
  GENERIC_LOAD_MESSAGE,
  NOT_FOUND_MESSAGE,
  loadErrorMessage,
} from './loadErrorMessage'

// The shape RTK Query's fetchBaseQuery hands back for a 400: the parsed
// ProblemDetails body under `data`. ExceptionHandlingMiddleware puts the
// domain message in `detail`, verbatim from GetWritingEntryHandler.
const notFoundRefusal = {
  status: 400,
  data: {
    title: 'Request rejected',
    detail: 'Writing entry not found.',
    status: 400,
  },
}

describe('loadErrorMessage', () => {
  it('says the entry may be gone when there is no error at all', () => {
    expect(loadErrorMessage(undefined)).toBe(NOT_FOUND_MESSAGE)
    expect(loadErrorMessage(null)).toBe(NOT_FOUND_MESSAGE)
  })

  it('says the entry may be gone for a 400 whose detail is the not-found message', () => {
    expect(loadErrorMessage(notFoundRefusal)).toBe(NOT_FOUND_MESSAGE)
  })

  it('keeps the generic message for a 400 with some other detail', () => {
    const validationFailure = {
      status: 400,
      data: {
        title: 'One or more validation errors occurred.',
        detail: 'Something else went wrong.',
        status: 400,
      },
    }
    expect(loadErrorMessage(validationFailure)).toBe(GENERIC_LOAD_MESSAGE)
  })

  it('keeps the generic message for a literal 404 -- this API never produces one for a missing entry', () => {
    // ExceptionHandlingMiddleware maps every DomainException to 400, so a real
    // 404 here means the route itself is missing (a deploy race), which a
    // retry can fix -- it must NOT read as "this night was deleted".
    expect(
      loadErrorMessage({ status: 404, data: { title: 'Not Found', status: 404 } }),
    ).toBe(GENERIC_LOAD_MESSAGE)
  })

  it('keeps the generic message for a 500', () => {
    expect(
      loadErrorMessage({
        status: 500,
        data: { title: 'An unexpected error occurred.', status: 500 },
      }),
    ).toBe(GENERIC_LOAD_MESSAGE)
  })

  it('survives a malformed or undefined error object without throwing', () => {
    expect(loadErrorMessage({})).toBe(GENERIC_LOAD_MESSAGE)
    expect(loadErrorMessage({ status: 'FETCH_ERROR', error: 'Failed to fetch' })).toBe(
      GENERIC_LOAD_MESSAGE,
    )
    expect(loadErrorMessage('boom')).toBe(GENERIC_LOAD_MESSAGE)
    expect(loadErrorMessage({ status: 400, data: { detail: 42 } })).toBe(GENERIC_LOAD_MESSAGE)
    expect(loadErrorMessage({ status: 400, data: null })).toBe(GENERIC_LOAD_MESSAGE)
  })
})
