import { useMemo, useState } from 'react'
import {
  useCookMealPlanBatchMutation,
  useDeleteMealPlanEntryMutation,
  useStockCheckBatchQuery,
} from '../../../shared/api/api'
import type { MealPlanEntryDto, StockCheckBatchDto } from '../../../shared/api/api'
import { useConfirm } from '../../../shared/hooks/useConfirm'
import { getErrorMessage } from '../../../shared/utils/getErrorMessage'

interface UseMealSlotDetailResult {
  selectedIds: Set<string>
  selectedArray: string[]
  toggle: (id: string, status: MealPlanEntryDto['status']) => void
  stockCheck: StockCheckBatchDto | undefined
  errorMessage: string | null
  isCooking: boolean
  isDeleting: boolean
  handleDelete: (entry: MealPlanEntryDto) => Promise<void>
  handleCook: (onClose: () => void) => Promise<void>
}

export function useMealSlotDetail(entries: MealPlanEntryDto[]): UseMealSlotDetailResult {
  const [deleteEntry, { isLoading: isDeleting }] = useDeleteMealPlanEntryMutation()
  const [cookBatch, { isLoading: isCooking }] = useCookMealPlanBatchMutation()
  const { confirm } = useConfirm()

  // Initialise with all Planned entries pre-checked so the common case
  // (cook everything planned) is one click.
  const [rawSelectedIds, setRawSelectedIds] = useState<Set<string>>(
    () => new Set(entries.filter((e) => e.status === 'Planned').map((e) => e.id)),
  )

  // `rawSelectedIds` is the raw user toggle state. The visible
  // `selectedIds` is derived by filtering out ids whose entry no longer
  // exists — done in a memo instead of an effect+setState so React
  // doesn't trigger an extra render after every entry-list change.
  const selectedIds = useMemo(() => {
    const filtered = new Set<string>()
    for (const id of rawSelectedIds) {
      if (entries.some((e) => e.id === id)) filtered.add(id)
    }
    return filtered
  }, [rawSelectedIds, entries])

  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const selectedArray = useMemo(() => Array.from(selectedIds), [selectedIds])

  const { data: stockCheck } = useStockCheckBatchQuery(
    { entryIds: selectedArray },
    { skip: selectedArray.length === 0 },
  )

  const toggle = (id: string, status: MealPlanEntryDto['status']) => {
    if (status !== 'Planned') return
    setRawSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const handleDelete = async (entry: MealPlanEntryDto) => {
    const ok = await confirm({
      title: 'ลบรายการ',
      message: (
        <>
          ลบ <strong>"{entry.recipeName}"</strong> ออกจากมื้อนี้?
        </>
      ),
      confirmText: 'ลบ',
      destructive: true,
    })
    if (!ok) return
    setErrorMessage(null)
    try {
      await deleteEntry(entry.id).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleCook = async (onClose: () => void) => {
    if (selectedArray.length === 0) return
    setErrorMessage(null)
    try {
      await cookBatch({ entryIds: selectedArray }).unwrap()
      onClose()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  return {
    selectedIds,
    selectedArray,
    toggle,
    stockCheck,
    errorMessage,
    isCooking,
    isDeleting,
    handleDelete,
    handleCook,
  }
}
