import {SpeedDialComponent} from '@syncfusion/ej2-react-buttons'
import type {SpeedDialItemModel} from '@syncfusion/ej2-buttons'
import {useMemo, useRef} from 'react'
import type {RailAction} from './ShortcutRailProvider'

/**
 * menunest-192: one button resting bottom-right, expanding VERTICALLY UPWARD.
 * `position` and `direction` are the component's own properties, so the corner
 * and the expansion need no custom positioning of ours.
 *
 * menunest-191 fixes the order — undo nearest the thumb, then redo, then
 * change history — and the caller supplies them already in that order, so this
 * component must not re-sort them.
 */
export function ShortcutRail({actions}: {actions: RailAction[]}) {
  // Keep the latest actions reachable from the Syncfusion callback without
  // re-creating the component when a handler identity changes.
  const actionsRef = useRef(actions)
  actionsRef.current = actions

  const items = useMemo<SpeedDialItemModel[]>(
    () =>
      actions.map(a => ({
        id: a.key,
        // menunest-200: the keyboard hint rides on the label, and the CSS hides
        // it below desktop widths where there is no keyboard to hint about.
        text: a.hint ? `${a.label} ${a.hint}` : a.label,
        title: a.label,
        disabled: a.disabled,
      })),
    [actions],
  )

  return (
    <div className="bdg-rail" data-testid="bdg-rail">
      <SpeedDialComponent
        position="BottomRight"
        mode="Linear"
        direction="Up"
        modal={true}
        content="⋮"
        items={items}
        cssClass="bdg-rail-dial"
        clicked={(args: {item?: SpeedDialItemModel}) => {
          const hit = actionsRef.current.find(a => a.key === args.item?.id)
          if (hit && !hit.disabled) hit.onPress()
        }}
      />
    </div>
  )
}
