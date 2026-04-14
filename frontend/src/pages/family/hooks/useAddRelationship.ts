import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useAddRelationshipMutation } from '../../../shared/api/api'

export interface AddRelationshipFormValues {
  fromUserId: string
  relationType: string
  toUserId: string
}

function getErrorMessage(err: unknown): string {
  if (typeof err === 'object' && err !== null && 'data' in err) {
    const data = (err as { data?: { detail?: string; title?: string } }).data
    if (data?.detail) return data.detail
    if (data?.title) return data.title
  }
  return 'เกิดข้อผิดพลาด กรุณาลองใหม่'
}

export function useAddRelationship() {
  const [isOpen, setIsOpen] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [addRelationship, { isLoading }] = useAddRelationshipMutation()

  const form = useForm<AddRelationshipFormValues>({
    defaultValues: { fromUserId: '', relationType: '', toUserId: '' },
  })

  const open = () => {
    form.reset()
    setErrorMessage(null)
    setIsOpen(true)
  }

  const close = () => {
    setIsOpen(false)
    form.reset()
    setErrorMessage(null)
  }

  const onSubmit = form.handleSubmit(async (values) => {
    setErrorMessage(null)
    try {
      await addRelationship({
        fromUserId: values.fromUserId,
        toUserId: values.toUserId,
        relationType: values.relationType,
      }).unwrap()
      close()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  })

  return { isOpen, open, close, form, isLoading, errorMessage, onSubmit }
}
