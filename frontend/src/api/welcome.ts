import { http, unwrap } from './auth'

export interface WelcomeGreeting {
  content: string
  featureNote: string | null
}

export const welcomeApi = {
  getGreeting: () => unwrap<WelcomeGreeting>(http.get('/welcome/greeting')),
}
