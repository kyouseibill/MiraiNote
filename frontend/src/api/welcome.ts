import { http, unwrap } from './auth'

export interface WelcomeNewsItem {
  title: string
  url: string
}

export interface WelcomeGreeting {
  /** 大标题，只含称呼，与 displayName 相同。 */
  content: string
  featureNote: string | null
  weatherWarning: string | null
  news: WelcomeNewsItem[]
  /** 昵称优先，否则用户名。 */
  displayName: string
  /** 上海日历日：`10月9日 · 周五`，有实况时再接 ` · 多云 24°C`。 */
  dateLine: string
  /** 日期行上的实况，例如 `多云 24°C`、`多云` 或 `24°C`。没有则 null。 */
  weatherBrief: string | null
  /** 今天的备忘摘要。没有则 null。 */
  memoSummary: string | null
}

export const welcomeApi = {
  getGreeting: () => unwrap<WelcomeGreeting>(http.get('/welcome/greeting')),
}
