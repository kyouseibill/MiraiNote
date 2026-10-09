import { http, unwrap } from './auth'

export interface WelcomePhrase {
  id: number
  kind: string
  text: string
  author: string | null
  source: string | null
  period: string | null
  special: string | null
  season: string | null
  isEnabled: boolean
  sortOrder: number
  updatedAt: string
}

export interface WelcomePhraseWrite {
  kind: string
  text: string
  author: string
  source: string
  period: string
  special: string
  season: string
  isEnabled: boolean
  sortOrder: number
}

export const welcomePhraseApi = {
  list: (kind?: string) => unwrap<WelcomePhrase[]>(http.get('/admin/welcome-phrases', {
    params: kind ? { kind } : undefined,
  })),
  create: (payload: WelcomePhraseWrite) => unwrap<WelcomePhrase>(http.post('/admin/welcome-phrases', payload)),
  update: (id: number, payload: WelcomePhraseWrite) =>
    unwrap<WelcomePhrase>(http.put(`/admin/welcome-phrases/${id}`, payload)),
  setEnabled: (id: number, isEnabled: boolean) =>
    unwrap<WelcomePhrase>(http.patch(`/admin/welcome-phrases/${id}/enabled`, { isEnabled })),
  remove: (id: number) => unwrap<null>(http.delete(`/admin/welcome-phrases/${id}`)),
}
