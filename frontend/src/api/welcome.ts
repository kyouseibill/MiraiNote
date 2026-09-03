import { http, unwrap } from './auth'

interface WelcomeGreetingResponse {
  content: string
}

function todayLocal(): string {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

export interface GetWelcomeGreetingOptions {
  date?: string
  /** 上次展示的欢迎语，供后端池随机时排除连续重复 */
  exclude?: string
}

export const welcomeApi = {
  getGreeting: (opts: GetWelcomeGreetingOptions = {}) =>
    unwrap<WelcomeGreetingResponse>(
      http.get('/welcome/greeting', {
        params: {
          date: opts.date ?? todayLocal(),
          ...(opts.exclude ? { exclude: opts.exclude } : {}),
        },
      }),
    ),
}
