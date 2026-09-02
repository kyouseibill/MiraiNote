import { http, unwrap } from './auth'

interface WelcomeGreetingResponse {
  content: string
}

export const welcomeApi = {
  getGreeting: () => unwrap<WelcomeGreetingResponse>(http.get('/welcome/greeting')),
}
