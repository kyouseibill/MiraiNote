export interface ChatSession {
  id: number
  title: string
  isArchived: boolean
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
  messages: ChatMessage[]
  createdAt: string
  updatedAt: string
}

export interface CreateSessionPayload {
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

export interface ChatAttachmentResponse {
  fileName: string
  fileType: string
  textContent: string
  fileSizeBytes: number
  mimeType?: string
  dataUrl?: string
  isImage?: boolean
}
