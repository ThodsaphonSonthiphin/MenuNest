import type {Page} from '@playwright/test'
import {meResponse, recordRequest, type RequestCapture} from './types'

/**
 * `/ai-assistant` IS behind FamilyRequiredRoute (src/router.tsx:79), so
 * /api/me must answer with a familyId or the page renders
 * "Could not load your profile." instead of the chat.
 *
 * Endpoints (api.ts:1190-1220):
 *   GET /api/chat/conversations                  → ConversationSummaryDto[]
 *   GET /api/chat/conversations/{id}/messages    → ChatMessageDto[]
 */

export const conversationsFixture = [
  {id: 'conv-1', title: 'เมนูเย็นนี้', createdAt: '2026-09-01T10:00:00Z', updatedAt: '2026-09-01T10:04:00Z'},
]

export const messagesFixture = [
  {
    id: 'msg-1', role: 'user',
    content: 'ตอนนี้มีไข่กับหมูสับ ทำอะไรกินดี',
    structuredData: null, createdAt: '2026-09-01T10:00:00Z',
  },
  {
    id: 'msg-2', role: 'assistant',
    content: 'จากของในสต็อก ทำได้ 2 เมนูครับ — ไข่เจียวหมูสับ และข้าวคลุกกะปิ ต้องการให้เพิ่มลงแผนมื้อเย็นวันนี้ไหม',
    structuredData: null, createdAt: '2026-09-01T10:00:12Z',
  },
]

interface ChatConfig {
  me: unknown
  conversations: unknown[]
  messages: unknown[]
}

export const createChatMocks = (page: Page, capture: RequestCapture) => {
  const config: ChatConfig = {
    me: meResponse,
    conversations: conversationsFixture,
    messages: messagesFixture,
  }

  const self = {
    me: (data: unknown) => {
      config.me = data
      return self
    },
    /** Pass [] to render the no-conversation empty state. */
    conversations: (rows: unknown[]) => {
      config.conversations = rows
      return self
    },
    messages: (rows: unknown[]) => {
      config.messages = rows
      return self
    },
    apply: async () => {
      await page.route(/\/api\/me(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({json: config.me})
      })
      await page.route(/\/api\/chat\/conversations\/[^/]+\/messages(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({json: config.messages})
      })
      await page.route(/\/api\/chat\/conversations(\?|$)/, async (route, request) => {
        await recordRequest(route, request, capture)
        await route.fulfill({json: config.conversations})
      })
    },
  }

  return self
}

export type ChatMocks = ReturnType<typeof createChatMocks>
