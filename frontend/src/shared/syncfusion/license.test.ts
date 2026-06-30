import { afterEach, describe, expect, it, vi } from 'vitest'

// Mock BOTH Syncfusion bases so we can assert the license is handed to each
// family without touching their (un-exported) internal validator state.
vi.mock('@syncfusion/react-base', () => ({ registerLicense: vi.fn() }))
vi.mock('@syncfusion/ej2-base', () => ({ registerLicense: vi.fn() }))

import { registerLicense as registerReactLicense } from '@syncfusion/react-base'
import { registerLicense as registerEj2License } from '@syncfusion/ej2-base'
import { registerSyncfusionLicense } from './license'

afterEach(() => {
  vi.clearAllMocks()
})

describe('registerSyncfusionLicense', () => {
  it('registers the key with BOTH react-base and ej2-base', () => {
    // Regression guard. The QR generator (ej2) showed the trial banner because
    // only react-base had been registered; ej2-base keeps a separate validator.
    // Dropping the ej2 registration brings the banner back on every ej2 page
    // (and, via SPA body-injection, everywhere visited afterwards). If this
    // assertion ever fails, that banner bug has returned.
    const families = registerSyncfusionLicense('FAKE-KEY')

    expect(registerReactLicense).toHaveBeenCalledWith('FAKE-KEY')
    expect(registerEj2License).toHaveBeenCalledWith('FAKE-KEY')
    expect(families).toEqual(['react', 'ej2'])
  })

  it('registers nothing when the key is empty (dev without a key)', () => {
    const families = registerSyncfusionLicense('')

    expect(registerReactLicense).not.toHaveBeenCalled()
    expect(registerEj2License).not.toHaveBeenCalled()
    expect(families).toEqual([])
  })

  it('registers nothing when the key is undefined', () => {
    registerSyncfusionLicense(undefined)

    expect(registerReactLicense).not.toHaveBeenCalled()
    expect(registerEj2License).not.toHaveBeenCalled()
  })
})
