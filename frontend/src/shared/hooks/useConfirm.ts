import { useContext } from 'react'
import { ConfirmContext } from '../components/ConfirmProvider'

/**
 * Returns an async `confirm(options)` that resolves to `true` when the
 * user clicks the confirm button and `false` on cancel / dismissal.
 * Wrap the app in <ConfirmProvider> for this to work.
 */
export function useConfirm() {
  const ctx = useContext(ConfirmContext)
  if (!ctx) {
    throw new Error('useConfirm must be used inside <ConfirmProvider>')
  }
  return ctx
}
