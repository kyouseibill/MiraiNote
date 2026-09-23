import { http, unwrap } from './auth'

export interface SkillSummary {
  name: string
  description: string
  enabled: boolean
  allowImplicitInvocation: boolean
  error: string | null
}

export interface SkillDocument extends SkillSummary {
  markdown: string
}

export interface SaveSkillPayload {
  name: string
  markdown: string
  allowImplicitInvocation: boolean
}

export const skillsApi = {
  list: () => unwrap<SkillSummary[]>(http.get('/skills')),
  get: (name: string) => unwrap<SkillDocument>(http.get(`/skills/${encodeURIComponent(name)}`)),
  create: (payload: SaveSkillPayload) => unwrap<SkillDocument>(http.post('/skills', payload)),
  update: (name: string, payload: SaveSkillPayload) =>
    unwrap<SkillDocument>(http.put(`/skills/${encodeURIComponent(name)}`, payload)),
  setEnabled: (name: string, enabled: boolean) =>
    unwrap<SkillDocument>(http.patch(`/skills/${encodeURIComponent(name)}/enabled`, { enabled })),
  remove: (name: string) => unwrap<null>(http.delete(`/skills/${encodeURIComponent(name)}`)),
}
