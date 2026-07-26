export interface LifeLog {
  id: number
  content: string
  mood: string | null
  imagePath: string | null
  imagePaths: string[]
  logDate: string
  createdAt: string
  updatedAt: string
}

export interface CreateLifeLogPayload {
  content: string
  mood?: string | null
  imagePath?: string | null
  imagePaths?: string[]
  logDate: string
}

export type UpdateLifeLogPayload = CreateLifeLogPayload

export interface LifeLogListQuery {
  page?: number
  pageSize?: number
  keyword?: string
  mood?: string
  month?: string
}
