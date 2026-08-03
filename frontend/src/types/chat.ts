export interface ChatSession {
  id: number
  title: string
  isArchived: boolean
  isPinned: boolean
  projectId?: number | null
  branchedFromSessionId?: number | null
  branchedFromMessageId?: number | null
  matchSnippet?: string | null
  createdAt: string
  updatedAt: string
}

export interface ChatMessage {
  id: number
  role: 'user' | 'assistant'
  content: string
  createdAt: string
}

export interface ChatSessionDetail {
  id: number
  title: string
  isArchived: boolean
  isPinned: boolean
  projectId?: number | null
  branchedFromSessionId?: number | null
  branchedFromMessageId?: number | null
  messages: ChatMessage[]
  createdAt: string
  updatedAt: string
}

export interface CreateSessionPayload {
  title?: string
  projectId?: number | null
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
}
