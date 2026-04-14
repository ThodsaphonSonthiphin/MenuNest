import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  useBuyShoppingListItemMutation,
  useUnbuyShoppingListItemMutation,
  useDeleteShoppingListItemMutation,
  useCompleteShoppingListMutation,
  useRegenerateShoppingListMutation,
  useAddShoppingListItemMutation,
} from '../../../shared/api/api'
import { useConfirm } from '../../../shared/hooks/useConfirm'
import { getErrorMessage } from '../../../shared/utils/getErrorMessage'

export function useShoppingListDetail(listId: string) {
  const navigate = useNavigate()
  const { confirm } = useConfirm()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const [buyItem, { isLoading: isBuying }] = useBuyShoppingListItemMutation()
  const [unbuyItem, { isLoading: isUnbuying }] = useUnbuyShoppingListItemMutation()
  const [deleteItem, { isLoading: isDeletingItem }] = useDeleteShoppingListItemMutation()
  const [completeList, { isLoading: isCompleting }] = useCompleteShoppingListMutation()
  const [regenerateList, { isLoading: isRegenerating }] = useRegenerateShoppingListMutation()
  const [addItem, { isLoading: isAddingItem }] = useAddShoppingListItemMutation()

  const handleBuy = async (itemId: string) => {
    setErrorMessage(null)
    try {
      await buyItem({ listId, itemId }).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleUnbuy = async (itemId: string) => {
    const ok = await confirm({
      title: 'ยกเลิกซื้อรายการ',
      message: 'ย้อนกลับรายการที่ซื้อแล้วหรือไม่? สต็อกจะลดลงตามปริมาณที่เคยเพิ่ม',
      confirmText: 'ยืนยัน',
      destructive: false,
    })
    if (!ok) return
    setErrorMessage(null)
    try {
      await unbuyItem({ listId, itemId }).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleDeleteItem = async (itemId: string, itemName: string) => {
    const ok = await confirm({
      title: 'ลบรายการ',
      message: (
        <>
          ลบ <strong>"{itemName}"</strong> ออกจากรายการหรือไม่?
        </>
      ),
      confirmText: 'ลบ',
      destructive: true,
    })
    if (!ok) return
    setErrorMessage(null)
    try {
      await deleteItem({ listId, itemId }).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleComplete = async () => {
    const ok = await confirm({
      title: 'เสร็จสิ้นรายการซื้อของ',
      message: 'ยืนยันว่าซื้อของเสร็จแล้ว? รายการจะถูกปิด',
      confirmText: '✓ เสร็จสิ้น',
      destructive: false,
    })
    if (!ok) return
    setErrorMessage(null)
    try {
      await completeList(listId).unwrap()
      navigate('/shopping')
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleRegenerate = async () => {
    const ok = await confirm({
      title: 'คำนวณรายการใหม่',
      message: 'items ที่ยังไม่ได้ซื้อจะถูกคำนวณใหม่ — items ที่ซื้อแล้วจะไม่เปลี่ยน',
      confirmText: '🔄 คำนวณใหม่',
      destructive: false,
    })
    if (!ok) return
    setErrorMessage(null)
    try {
      await regenerateList(listId).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleAddItem = async (ingredientId: string, quantity: number) => {
    setErrorMessage(null)
    try {
      await addItem({ listId, ingredientId, quantity }).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  return {
    errorMessage,
    isBuying,
    isUnbuying,
    isDeletingItem,
    isCompleting,
    isRegenerating,
    isAddingItem,
    handleBuy,
    handleUnbuy,
    handleDeleteItem,
    handleComplete,
    handleRegenerate,
    handleAddItem,
  }
}
