import { createContext, useCallback, useMemo, useRef, useState, type ReactNode } from 'react'
import { Dialog } from '@syncfusion/react-popups'
import { Button, Color, Variant } from '@syncfusion/react-buttons'

// ----------------------------------------------------------------------
// Public API — use `useConfirm()` to get the `confirm(options)` function
// that returns a Promise<boolean>. Replaces window.confirm with a
// Syncfusion-styled modal that matches the rest of the app.
// ----------------------------------------------------------------------

export interface ConfirmOptions {
  title?: string
  message: ReactNode
  confirmText?: string
  cancelText?: string
  /** Renders the primary button in red — use for deletes / destructive actions. */
  destructive?: boolean
}

type Resolver = (value: boolean) => void

interface ConfirmContextValue {
  confirm: (options: ConfirmOptions) => Promise<boolean>
}

export const ConfirmContext = createContext<ConfirmContextValue | null>(null)

interface State {
  open: boolean
  options: ConfirmOptions
}

const defaultState: State = {
  open: false,
  options: { message: '' },
}

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<State>(defaultState)
  const resolverRef = useRef<Resolver | null>(null)

  const confirm = useCallback((options: ConfirmOptions) => {
    return new Promise<boolean>((resolve) => {
      resolverRef.current = resolve
      setState({ open: true, options })
    })
  }, [])

  const settle = useCallback((value: boolean) => {
    resolverRef.current?.(value)
    resolverRef.current = null
    setState((s) => ({ ...s, open: false }))
  }, [])

  const value = useMemo<ConfirmContextValue>(() => ({ confirm }), [confirm])

  const {
    title = 'ยืนยันการทำรายการ',
    message,
    confirmText = 'ยืนยัน',
    cancelText = 'ยกเลิก',
    destructive = false,
  } = state.options

  return (
    <ConfirmContext.Provider value={value}>
      {children}
      <Dialog
        open={state.open}
        onClose={() => settle(false)}
        modal
        header={title}
        style={{ width: '420px' }}
      >
        <div style={{ fontSize: 14, lineHeight: 1.6, color: 'var(--color-text, #222)' }}>
          {message}
        </div>
        <div
          style={{
            display: 'flex',
            gap: 8,
            justifyContent: 'flex-end',
            marginTop: 20,
          }}
        >
          <Button
            type="button"
            variant={Variant.Outlined}
            color={Color.Secondary}
            onClick={() => settle(false)}
          >
            {cancelText}
          </Button>
          <Button
            type="button"
            variant={Variant.Filled}
            color={destructive ? Color.Error : Color.Primary}
            onClick={() => settle(true)}
          >
            {confirmText}
          </Button>
        </div>
      </Dialog>
    </ConfirmContext.Provider>
  )
}
