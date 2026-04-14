import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { useCreateShoppingListMutation } from '../../../shared/api/api'
import { getErrorMessage } from '../../../shared/utils/getErrorMessage'
import { useAppDispatch } from '../../../store'
import { closeCreateDialog } from '../shoppingSlice'

export interface CreateListFormValues {
  name: string
  useDateRange: boolean
  fromDate: string
  toDate: string
}

export function useCreateShoppingList() {
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const [createShoppingList, { isLoading }] = useCreateShoppingListMutation()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const todayThai = new Date().toLocaleDateString('th-TH', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  })

  const form = useForm<CreateListFormValues>({
    defaultValues: {
      name: `ซื้อของ ${todayThai}`,
      useDateRange: false,
      fromDate: '',
      toDate: '',
    },
  })

  const handleClose = () => {
    dispatch(closeCreateDialog())
    form.reset({
      name: `ซื้อของ ${todayThai}`,
      useDateRange: false,
      fromDate: '',
      toDate: '',
    })
    setErrorMessage(null)
  }

  const onSubmit = form.handleSubmit(async (values) => {
    setErrorMessage(null)
    try {
      const result = await createShoppingList({
        name: values.name.trim(),
        fromDate: values.useDateRange ? values.fromDate || undefined : undefined,
        toDate: values.useDateRange ? values.toDate || undefined : undefined,
      }).unwrap()
      handleClose()
      navigate(`/shopping/${result.id}`)
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  })

  return {
    form,
    isLoading,
    errorMessage,
    onSubmit,
    handleClose,
  }
}
