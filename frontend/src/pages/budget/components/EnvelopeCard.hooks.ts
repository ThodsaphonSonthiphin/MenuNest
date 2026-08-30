import {useEffect, useRef, useState} from 'react'
import {useAppDispatch, useAppSelector} from '../../../store'
import {setExpandedCategory} from '../budgetSlice'
import {useSetAssignedAmountMutation, type BudgetAccountDto, type EnvelopeDto} from '../../../shared/api/api'
import {getViewerTimeZone} from '../../../shared/utils/timeZone'

const LONG_PRESS_MS = 450
const MOVE_TOLERANCE_PX = 8

export interface UseEnvelopeCardArgs {
  cat: EnvelopeDto
  /**
   * menunest-202: the Credit account this envelope pays, when
   * `cat.paymentForAccountId` names one. The card needs the account's balance
   * (the ยอดบัตร half of the shortfall line) and its type (menunest-212's
   * action word), neither of which is on the EnvelopeDto.
   */
  account?: BudgetAccountDto | null
  onAddTransaction: (categoryId: string) => void
  onMoveMoney: (cat: EnvelopeDto) => void
  onCoverOverspending: (cat: EnvelopeDto) => void
  /** Opens PaymentDialog. Only ever called for a Payment envelope. */
  onPay?: (cat: EnvelopeDto, account: BudgetAccountDto) => void
}

export function useEnvelopeCard({
  cat, account, onAddTransaction, onMoveMoney, onCoverOverspending, onPay,
}: UseEnvelopeCardArgs) {
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

  const isPayment = cat.paymentForAccountId !== null && cat.paymentForAccountId !== undefined
  const payAccount = isPayment ? (account ?? null) : null

  // menunest-204: "You never add a plain transaction to this Envelope." The
  // long-press shortcut therefore opens the payment sheet on a Payment
  // envelope instead of the transaction dialog — routing it to
  // onAddTransaction would offer exactly the one write the card forbids.
  const openPrimary = () => {
    if (payAccount) onPay?.(cat, payAccount)
    else onAddTransaction(cat.categoryId)
  }

  const onPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    longPressedRef.current = false
    downAtRef.current = {x: e.clientX, y: e.clientY}
    timerRef.current = window.setTimeout(() => {
      longPressedRef.current = true
      openPrimary()
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
      setAssigned({categoryId: cat.categoryId, year, month, amount: assignedDraft, timeZoneId: getViewerTimeZone()})
    }
  }
  const revertAssigned = () => setAssignedDraft(cat.assigned)

  return {
    expanded,
    /** True ⇒ render the Payment-envelope card (menunest-202/205). */
    isPayment,
    /** The Credit account being paid — null when the summary has no row for it. */
    payAccount,
    assignedDraft, setAssignedDraft,
    commitAssigned, revertAssigned,
    onPointerDown, onPointerMove, onPointerUp, onPointerCancel, onTap,
    onAddTransaction: () => onAddTransaction(cat.categoryId),
    onMoveMoney: () => onMoveMoney(cat),
    onCoverOverspending: () => onCoverOverspending(cat),
    onPay: () => { if (payAccount) onPay?.(cat, payAccount) },
  }
}
