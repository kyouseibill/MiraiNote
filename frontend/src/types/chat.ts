export interface ChatSession {
  id: number
  title: string
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
  messages: ChatMessage[]
  createdAt: string
  updatedAt: string
}

export interface CreateSessionPayload {
  title?: string
}

export interface SendMessagePayload {
  content: string
}
