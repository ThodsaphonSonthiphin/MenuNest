import { useCallback } from 'react'
import {
  useListConversationsQuery,
  useCreateConversationMutation,
  useDeleteConversationMutation,
  useGetChatMessagesQuery,
  useSendChatMessageMutation,
} from '../../../shared/api/api'
import { useAppDispatch, useAppSelector } from '../../../store'
import { setActiveConversation } from '../aiAssistantSlice'

export function useAiAssistant() {
  const dispatch = useAppDispatch()
  const activeConversationId = useAppSelector((s) => s.aiAssistant.activeConversationId)

  const { data: conversations, isLoading: isLoadingConversations } = useListConversationsQuery()
  const { data: messages, isLoading: isLoadingMessages } = useGetChatMessagesQuery(
    activeConversationId!, { skip: !activeConversationId },
  )
  const [createConversation] = useCreateConversationMutation()
  const [deleteConversation] = useDeleteConversationMutation()
  const [sendMessage, { isLoading: isSending }] = useSendChatMessageMutation()

  const handleNewConversation = useCallback(async () => {
    const result = await createConversation().unwrap()
    dispatch(setActiveConversation(result.id))
  }, [createConversation, dispatch])

  const handleSelectConversation = useCallback((id: string) => {
    dispatch(setActiveConversation(id))
  }, [dispatch])

  const handleDeleteConversation = useCallback(async (id: string) => {
    await deleteConversation(id).unwrap()
    if (activeConversationId === id) dispatch(setActiveConversation(null))
  }, [deleteConversation, activeConversationId, dispatch])

  const handleSendMessage = useCallback(async (content: string) => {
    if (!activeConversationId || !content.trim()) return
    await sendMessage({ conversationId: activeConversationId, content: content.trim() }).unwrap()
  }, [activeConversationId, sendMessage])

  return {
    conversations: conversations ?? [],
    messages: messages ?? [],
    activeConversationId,
    isLoadingConversations,
    isLoadingMessages,
    isSending,
    handleNewConversation,
    handleSelectConversation,
    handleDeleteConversation,
    handleSendMessage,
  }
}
