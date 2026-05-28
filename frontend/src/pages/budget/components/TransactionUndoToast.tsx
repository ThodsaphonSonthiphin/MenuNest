import {useEffect, useRef} from 'react'

interface Props {
  message: string
  onUndo: () => void
  onTimeout: () => void
  durationMs?: number
}

/**
 * Bottom-fixed toast with a 5-second countdown bar and an Undo button.
 * The parent mounts this component when a delete is pending and unmounts
 * it after Undo or after onTimeout fires. To start a fresh countdown for
 * a second pending delete, give the component a new `key`.
 */
export function TransactionUndoToast({message, onUndo, onTimeout, durationMs = 5000}: Props) {
  // Stash the timeout id so the cleanup can clear it. We deliberately keep
  // the effect's dep list empty so the timer is created exactly once per
  // mount — the parent forces a re-mount via `key` when it wants a new
  // countdown.
  const firedRef = useRef(false)

  useEffect(() => {
    const id = window.setTimeout(() => {
      firedRef.current = true
      onTimeout()
    }, durationMs)
    return () => {
      window.clearTimeout(id)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const handleUndo = () => {
    if (firedRef.current) return  // race: onTimeout already fired
    onUndo()
  }

  return (
    <div className="bdg-undo-toast" data-testid="bdg-undo-toast" role="status" aria-live="polite">
      <span className="bdg-undo-toast-msg">{message}</span>
      <button
        type="button"
        className="bdg-undo-toast-btn"
        data-testid="bdg-undo-btn"
        onClick={handleUndo}
      >
        Undo
      </button>
      <div
        className="bdg-undo-toast-bar"
        style={{animationDuration: `${durationMs}ms`}}
        aria-hidden="true"
      />
    </div>
  )
}
