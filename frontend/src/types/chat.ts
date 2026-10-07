import type { HouseholdChatDraft } from '@/utils/householdChat'

export interface ChatSession {
  id: number
  title: string
  isArchived: boolean
  isPinned: boolean
  projectId?: number | null
  branchedFromSessionId?: number | null
  branchedFromMessageId?: number | null
  matchSnippet?: string | null
  /** provider:modelId 格式；会话创建后不可修改。 */
  modelKey?: string | null
  createdAt: string
  updatedAt: string
}

export type ToolCallStatus = 'running' | 'success' | 'failure'

/** 单次工具调用事件（流式实时 + 本会话内回放；历史接口暂不持久化） */
export interface ToolCallEvent {
  id: string
  name: string
  label: string
  status: ToolCallStatus
  detail?: string
  inputSummary?: string
  resultSummary?: string
  errorDetail?: string
  elapsedSeconds?: number
  /** household_chat 的待确认草稿。工具本身不写完成记录。 */
  householdDraft?: HouseholdChatDraft
}

export interface ChatMessage {
  id: number
  role: 'user' | 'assistant'
  content: string
  createdAt: string
  /** 本轮流式产生的工具事件卡；仅前端会话内保留，服务端历史无此字段 */
  toolEvents?: ToolCallEvent[]
}

export interface ChatSessionDetail {
  id: number
  title: string
  isArchived: boolean
  isPinned: boolean
  projectId?: number | null
  branchedFromSessionId?: number | null
  branchedFromMessageId?: number | null
  modelKey?: string | null
  messages: ChatMessage[]
  createdAt: string
  updatedAt: string
}

export interface CreateSessionPayload {
  title?: string
  projectId?: number | null
  modelKey?: string | null
}

export interface AiModel {
  key: string
  provider: string
  providerDisplayName: string
  modelId: string
  displayName: string
  supportsChat: boolean
  supportsWork: boolean
  supportsTools: boolean
}

export interface ChatProject {
  id: number
  name: string
  instructions: string
  color: string
  icon: string
  sessionCount: number
  createdAt: string
  updatedAt: string
}

export interface ChatProjectPayload {
  name: string
  instructions?: string
  color?: string
  icon?: string
}

export interface BranchSessionPayload {
  messageId?: number | null
  title?: string
}

export interface ChatAttachmentContent {
  fileName: string
  fileType: string
  textContent: string
  mimeType?: string
  dataUrl?: string
  isImage?: boolean
  /** 私有工作区相对路径，例如 uploads/2026/10/文件.pdf */
  storedPath?: string
}

export interface SendMessagePayload {
  content: string
  attachments?: ChatAttachmentContent[]
}

export interface TemporaryChatHistoryMessage {
  role: 'user' | 'assistant'
  content: string
}

export interface TemporarySendMessagePayload extends SendMessagePayload {
  history: TemporaryChatHistoryMessage[]
}

export interface ChatAttachmentResponse {
  fileName: string
  fileType: string
  textContent: string
  fileSizeBytes: number
  mimeType?: string
  dataUrl?: string
  isImage?: boolean
  storedPath?: string
}
