import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'

interface AiAssistantState {
  activeConversationId: string | null
  isRecording: boolean
}

const initialState: AiAssistantState = {
  activeConversationId: null,
  isRecording: false,
}

const aiAssistantSlice = createSlice({
  name: 'aiAssistant',
  initialState,
  reducers: {
    setActiveConversation(state, action: PayloadAction<string | null>) {
      state.activeConversationId = action.payload
    },
    setIsRecording(state, action: PayloadAction<boolean>) {
      state.isRecording = action.payload
    },
  },
})

export const { setActiveConversation, setIsRecording } = aiAssistantSlice.actions
export default aiAssistantSlice.reducer
