import { http, unwrap } from './auth'

export interface WelcomeNewsItem {
  title: string
  url: string
}

export interface WelcomeGreeting {
  content: string
  featureNote: string | null
  weatherWarning: string | null
  news: WelcomeNewsItem[]
}

export const welcomeApi = {
  getGreeting: () => unwrap<WelcomeGreeting>(http.get('/welcome/greeting')),
}
