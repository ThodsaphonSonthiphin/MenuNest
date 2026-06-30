import { registerLicense } from '@syncfusion/react-base'
import { registerLicense as registerEj2License } from '@syncfusion/ej2-base'

/**
 * Register the Syncfusion Community License with BOTH package families we use.
 *
 * We mix two Syncfusion families: Pure React (`@syncfusion/react-*`, used
 * almost everywhere) and legacy EJ2 (`@syncfusion/ej2-react-*`, currently only
 * the QR generator on the Family / Health pages). Each family keeps its OWN
 * module-scoped license validator — registering with `react-base` does NOT
 * register `ej2-base`. Skip the ej2 registration and any ej2 component injects
 * the trial banner into `document.body`; because this is an SPA (React Router
 * never reloads) that banner then persists across every subsequent route. So
 * we register the SAME key with BOTH bases.
 *
 * @param key the `VITE_SYNCFUSION_LICENSE_KEY` value (undefined/empty in dev).
 * @returns the families the key was registered with — `[]` when no key is set.
 *   Returned so the unit test can assert the dual registration without reaching
 *   into Syncfusion's internal (un-exported) validator state.
 */
export function registerSyncfusionLicense(
  key: string | undefined,
): readonly ('react' | 'ej2')[] {
  if (!key) return []
  registerLicense(key)
  registerEj2License(key)
  return ['react', 'ej2']
}
