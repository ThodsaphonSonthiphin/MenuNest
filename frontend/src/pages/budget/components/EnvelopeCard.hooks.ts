import {useEffect, useRef, useState} from 'react'
import {useAppDispatch, useAppSelector} from '../../../store'
import {setExpandedCategory} from '../budgetSlice'
import {useSetAssignedAmountMutation, type EnvelopeDto} from '../../../shared/api/api'

const LONG_PRESS_MS = 450
const MOVE_TOLERANCE_PX = 8

export interface UseEnvelopeCardArgs {
  cat: EnvelopeDto
  onAddTransaction: (categoryId: string) => void
  onMoveMoney: (cat: EnvelopeDto) => void
  onCoverOverspending: (cat: EnvelopeDto) => void
}

export function useEnvelopeCard({cat, onAddTransaction, onMoveMoney, onCoverOverspending}: UseEnvelopeCardArgs) {
  const dispatch = useAppDispatch()
  const {year, month, expandedCategoryId} = useAppSelector(s => s.budget)
  const expanded = expandedCategoryId === cat.categoryId
  const [setAssigned] = useSetAssignedAmountMutation()
  const [assignedDraft, setAssignedDraft] = useState<number>(cat.assigned)

  useEffect(() => { setAssignedDraft(cat.assigned) }, [cat.assigned])

  // Long-press detection — start a timer on pointerdown, cancel on
  // move-too-far or pointerup. If the timer fires, we mark `longPressed`
  // so the subsequent click doesn't also toggle expansion.
  const longPressedRef = useRef(false)
  const downAtRef = useRef<{x: number; y: number} | null>(null)
  const timerRef = useRef<number | null>(null)

  const onPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    longPressedRef.current = false
    downAtRef.current = {x: e.clientX, y: e.clientY}
    timerRef.current = window.setTimeout(() => {
      longPressedRef.current = true
      onAddTransaction(cat.categoryId)
    }, LONG_PRESS_MS)
  }
  const onPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!downAtRef.current || timerRef.current === null) return
    const dx = Math.abs(e.clientX - downAtRef.current.x)
    const dy = Math.abs(e.clientY - downAtRef.current.y)
    if (dx > MOVE_TOLERANCE_PX || dy > MOVE_TOLERANCE_PX) {
      window.clearTimeout(timerRef.current)
      timerRef.current = null
    }
  }
  const cancelLongPress = () => {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current)
      timerRef.current = null
    }
    downAtRef.current = null
  }
  const onPointerUp = () => cancelLongPress()
  const onPointerCancel = () => cancelLongPress()

  const onTap = () => {
    if (longPressedRef.current) {
      longPressedRef.current = false
      return // long-press already fired; do not toggle
    }
    dispatch(setExpandedCategory(expanded ? null : cat.categoryId))
  }

  const commitAssigned = () => {
    if (assignedDraft !== cat.assigned) {
      setAssigned({categoryId: cat.categoryId, year, month, amount: assignedDraft})
    }
  }
  const revertAssigned = () => setAssignedDraft(cat.assigned)

  return {
    expanded,
    assignedDraft, setAssignedDraft,
    commitAssigned, revertAssigned,
    onPointerDown, onPointerMove, onPointerUp, onPointerCancel, onTap,
    onAddTransaction: () => onAddTransaction(cat.categoryId),
    onMoveMoney: () => onMoveMoney(cat),
    onCoverOverspending: () => onCoverOverspending(cat),
  }
}
