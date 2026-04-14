import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  useRotateInviteCodeMutation,
  useLeaveFamilyMutation,
  useDeleteRelationshipMutation,
} from '../../../shared/api/api'

function getErrorMessage(err: unknown): string {
  if (typeof err === 'object' && err !== null && 'data' in err) {
    const data = (err as { data?: { detail?: string; title?: string } }).data
    if (data?.detail) return data.detail
    if (data?.title) return data.title
  }
  return 'เกิดข้อผิดพลาด กรุณาลองใหม่'
}

export function useFamilyPage() {
  const navigate = useNavigate()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const [rotateCode, { isLoading: isRotating }] = useRotateInviteCodeMutation()
  const [leaveFamily, { isLoading: isLeaving }] = useLeaveFamilyMutation()
  const [deleteRelationship, { isLoading: isDeletingRelationship }] =
    useDeleteRelationshipMutation()

  const handleRotateCode = async () => {
    setErrorMessage(null)
    try {
      await rotateCode().unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleLeaveFamily = async () => {
    setErrorMessage(null)
    try {
      await leaveFamily().unwrap()
      navigate('/join-family', { replace: true })
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleDeleteRelationship = async (id: string) => {
    setErrorMessage(null)
    try {
      await deleteRelationship(id).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  return {
    errorMessage,
    isRotating,
    isLeaving,
    isDeletingRelationship,
    handleRotateCode,
    handleLeaveFamily,
    handleDeleteRelationship,
  }
}
