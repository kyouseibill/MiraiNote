import { http, unwrap } from './auth'

interface WelcomeGreetingResponse {
  content: string
}

export interface GetWelcomeGreetingOptions {
  /** 预留。传了就会改走文案池且不调模型。正常首页不要传。 */
  exclude?: string
}

export const welcomeApi = {
  getGreeting: (opts: GetWelcomeGreetingOptions = {}) =>
    unwrap<WelcomeGreetingResponse>(
      http.get('/welcome/greeting', {
        params: opts.exclude ? { exclude: opts.exclude } : {},
      }),
    ),
}
