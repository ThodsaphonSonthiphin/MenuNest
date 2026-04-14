import { useCallback, useMemo, useRef, useState } from 'react'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { useAiAssistant } from './hooks/useAiAssistant'
import { useAzureSpeech } from './hooks/useAzureSpeech'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { RecipeCard } from './components/RecipeCard'
import { ConfirmationMessage } from './components/ConfirmationMessage'
import type { ChatMessageDto } from '../../shared/api/api'

export function AiAssistantPage() {
  const { displayName } = useCurrentUser()
  const {
    conversations,
    messages,
    activeConversationId,
    isLoadingMessages,
    isSending,
    handleNewConversation,
    handleSelectConversation,
    handleDeleteConversation,
    handleSendMessage,
  } = useAiAssistant()

  const { isListening, transcript, error: speechError, startListening, stopListening, setTranscript } =
    useAzureSpeech()

  const [inputValue, setInputValue] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)

  // displayName is used for identifying the current user in aria/title contexts
  void displayName

  const onSend = useCallback(async () => {
    const text = inputValue.trim() || transcript.trim()
    if (!text) return

    setInputValue('')
    setTranscript('')
    await handleSendMessage(text)
  }, [inputValue, transcript, handleSendMessage, setTranscript])

  const onMicPress = useCallback(() => {
    if (isListening) {
      stopListening()
    } else {
      startListening()
    }
  }, [isListening, startListening, stopListening])

  const handleConfirm = useCallback(() => {
    handleSendMessage('ได้เลย ยืนยัน')
  }, [handleSendMessage])

  const handleReject = useCallback(() => {
    handleSendMessage('ยกเลิก ไม่ต้องทำ')
  }, [handleSendMessage])

  // Parse structured data from the last message
  const lastMessage = messages[messages.length - 1]
  const structuredData = useMemo(() => {
    if (!lastMessage?.structuredData) return null
    try {
      return JSON.parse(lastMessage.structuredData)
    } catch {
      return null
    }
  }, [lastMessage])

  return (
    <section className="page page--ai-assistant">
      <header className="page__header">
        <h1>AI Assistant</h1>
        <Button variant={Variant.Filled} color={Color.Primary} onClick={handleNewConversation}>
          + บทสนทนาใหม่
        </Button>
      </header>

      <div className="ai-assistant-layout">
        {/* Conversation list */}
        <aside className="ai-assistant-sidebar">
          {conversations.map((c) => (
            <div
              key={c.id}
              className={`ai-conversation-item ${c.id === activeConversationId ? 'ai-conversation-item--active' : ''}`}
              onClick={() => handleSelectConversation(c.id)}
            >
              <span className="ai-conversation-item__title">{c.title}</span>
              <button
                className="ai-conversation-item__delete"
                onClick={(e) => {
                  e.stopPropagation()
                  handleDeleteConversation(c.id)
                }}
              >
                ×
              </button>
            </div>
          ))}
        </aside>

        {/* Chat area */}
        <div className="ai-assistant-chat">
          {!activeConversationId ? (
            <div className="ai-assistant-empty">
              <p>เลือกบทสนทนาหรือสร้างใหม่เพื่อเริ่มคุยกับ AI</p>
            </div>
          ) : (
            <>
              {isLoadingMessages ? (
                <div className="ai-assistant-loading">กำลังโหลด...</div>
              ) : (
                <div className="ai-chat-messages">
                  {messages.map((m) => (
                    <div
                      key={m.id}
                      className={`ai-chat-bubble ai-chat-bubble--${m.role.toLowerCase()}`}
                    >
                      <div className="ai-chat-bubble__content">{m.content}</div>
                      {m.structuredData && renderStructuredData(m)}
                    </div>
                  ))}
                  {isSending && (
                    <div className="ai-chat-bubble ai-chat-bubble--assistant ai-chat-bubble--loading">
                      <span>กำลังคิด...</span>
                    </div>
                  )}
                </div>
              )}

              {/* Structured data actions for last message */}
              {structuredData?.type === 'confirmation' && (
                <ConfirmationMessage
                  onConfirm={handleConfirm}
                  onReject={handleReject}
                  disabled={isSending}
                />
              )}

              {/* Input bar */}
              <div className="ai-input-bar">
                <input
                  ref={inputRef}
                  className="ai-input-bar__text"
                  type="text"
                  placeholder="ถามเรื่องอาหาร..."
                  value={isListening ? transcript : inputValue}
                  onChange={(e) => setInputValue(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && !e.shiftKey && onSend()}
                  disabled={isSending}
                />
                <button
                  className={`ai-input-bar__mic ${isListening ? 'ai-input-bar__mic--active' : ''}`}
                  onMouseDown={onMicPress}
                  onMouseUp={() => isListening && stopListening()}
                  onTouchStart={onMicPress}
                  onTouchEnd={() => isListening && stopListening()}
                  disabled={isSending}
                >
                  🎤
                </button>
                <button
                  className="ai-input-bar__send"
                  onClick={onSend}
                  disabled={isSending || (!inputValue.trim() && !transcript.trim())}
                >
                  ➤
                </button>
              </div>
              {speechError && <p className="ai-speech-error">{speechError}</p>}
            </>
          )}
        </div>
      </div>
    </section>
  )

  function renderStructuredData(msg: ChatMessageDto) {
    if (!msg.structuredData) return null
    try {
      const data = JSON.parse(msg.structuredData)
      if (data.type === 'recipe_cards' && data.cards) {
        return (
          <div className="ai-recipe-cards">
            {data.cards.map((card: { recipeId: string; name: string; stockMatch?: string }) => (
              <RecipeCard key={card.recipeId} {...card} />
            ))}
          </div>
        )
      }
      return null
    } catch {
      return null
    }
  }
}
