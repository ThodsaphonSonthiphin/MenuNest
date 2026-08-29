import {SpeedDialComponent} from '@syncfusion/ej2-react-buttons'
import type {SpeedDialItemModel} from '@syncfusion/ej2-buttons'
import {useCallback, useEffect, useMemo, useRef, useState} from 'react'
import {classifyUndoKey} from '../lib/keyBinding'
import {decideRailVisibility, initialRailScrollState} from '../lib/railVisibility'
import type {RailAction} from './ShortcutRailProvider'

/** How long the rail waits before coming back on its own. */
const IDLE_RETURN_MS = 900

/**
 * menunest-192: one button resting bottom-right, expanding VERTICALLY UPWARD,
 * hiding on a downward flick and returning on an upward one or on idle. It is
 * NOT draggable — there is deliberately no drag code here.
 *
 * menunest-191 fixes the item order (undo nearest the thumb, then redo, then
 * change history); the caller supplies them in that order and this component
 * must not re-sort them.
 *
 * The two decisions worth testing live in ../lib/railVisibility and
 * ../lib/keyBinding, because the frontend has no component test harness and a
 * decision buried in a handler here would have no coverage at all.
 */
export function ShortcutRail({actions}: {actions: RailAction[]}) {
  // Keep the latest actions reachable from the Syncfusion callback and the key
  // handler without re-subscribing either when a handler identity changes.
  const actionsRef = useRef(actions)
  actionsRef.current = actions

  const [scroll, setScroll] = useState(initialRailScrollState)
  // Mirrored as a ref for the scroll handler (which must not re-subscribe) and
  // as state for the class that swaps the main glyph.
  const [isOpen, setIsOpen] = useState(false)
  const isOpenRef = useRef(false)
  const idleTimerRef = useRef<number | null>(null)

  const items = useMemo<SpeedDialItemModel[]>(
    () =>
      actions.map(a => ({
        id: a.key,
        // `text` is what Syncfusion would paint inside the 44px circle. The
        // approved mock puts the ICON there and the label in a pill to the
        // left, so the real content comes from itemTemplate below; this is
        // kept only for the native tooltip and for accessible naming.
        title: a.label,
        disabled: a.disabled,
      })),
    [actions],
  )

  // The mock's item: a glyph in the circle, and a dark label pill to its left
  // carrying the name plus — desktop only (menunest-200) — the key binding.
  const itemTemplate = useCallback((item: SpeedDialItemModel) => {
    // EJ2 hands the template its internal complex object, not the plain model
    // that was passed in: the declared values sit one level down on
    // `properties`, and a bare `item.id` reads undefined.
    const id = item?.id ?? (item as {properties?: SpeedDialItemModel})?.properties?.id
    const a = actionsRef.current.find(x => x.key === id)
    if (!a) return <span />
    return (
      <span className="bdg-rail-item">
        <span className="bdg-rail-ico" aria-hidden="true">{a.icon}</span>
        <span className="bdg-rail-tag">
          {a.label}
          {a.hint ? <span className="bdg-rail-key">{a.hint}</span> : null}
        </span>
      </span>
    )
  }, [])

  // ----- hide on scroll, return on idle (menunest-192) -----
  useEffect(() => {
    function onScroll() {
      setScroll(prev =>
        decideRailVisibility(prev, {
          scrollTop: window.scrollY,
          isOpen: isOpenRef.current,
        }),
      )
      if (idleTimerRef.current !== null) window.clearTimeout(idleTimerRef.current)
      idleTimerRef.current = window.setTimeout(
        () => setScroll(p => ({...p, hidden: false})),
        IDLE_RETURN_MS,
      )
    }

    window.addEventListener('scroll', onScroll, {passive: true})
    return () => {
      window.removeEventListener('scroll', onScroll)
      if (idleTimerRef.current !== null) window.clearTimeout(idleTimerRef.current)
    }
  }, [])

  // ----- Ctrl+Z / Cmd+Z (menunest-200) -----
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      const el = document.activeElement as HTMLElement | null
      const inEditable =
        !!el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable)

      // menunest-200 left this choice to the build. The DOM check wins over
      // having each dialog register itself: the five budget dialogs are local
      // useState inside five components, and a guard that needs no edit to any
      // of them is less to keep in step. Swap to registration if a dialog ever
      // renders without this class.
      const dialogOpen = !!document.querySelector('.budget-modal-overlay')

      const verdict = classifyUndoKey(e, {inEditable, dialogOpen})
      if (verdict === 'ignore') return

      const hit = actionsRef.current.find(a => a.key === verdict)
      if (!hit || hit.disabled) return

      e.preventDefault()
      hit.onPress()
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [])

  return (
    <div
      className={`bdg-rail ${scroll.hidden ? 'is-hidden' : ''} ${isOpen ? 'is-open' : ''}`}
      data-testid="bdg-rail"
    >
      <SpeedDialComponent
        position="BottomRight"
        mode="Linear"
        direction="Up"
        modal={true}
        // One icon span whose glyph is chosen in CSS off .is-open. Syncfusion's
        // own openIconCss/closeIconCss pair is meant to do this, but it does not
        // swap here — measured open, the button kept the resting glyph.
        openIconCss="bdg-rail-glyph"
        items={items}
        itemTemplate={itemTemplate}
        cssClass="bdg-rail-dial"
        // Forwarded onto the rendered <button>. The wrapper above is
        // `display: contents` (Syncfusion positions the button itself), so it
        // has no box — e2e geometry assertions need a testid on the real one.
        data-testid="bdg-rail-fab"
        beforeOpen={() => {
          isOpenRef.current = true
          setIsOpen(true)
          setScroll(p => ({...p, hidden: false}))
        }}
        beforeClose={() => { isOpenRef.current = false; setIsOpen(false) }}
        clicked={(args: {item?: SpeedDialItemModel}) => {
          const hit = actionsRef.current.find(a => a.key === args.item?.id)
          if (hit && !hit.disabled) hit.onPress()
        }}
      />
    </div>
  )
}
