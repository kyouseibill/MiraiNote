import { http, unwrap } from './auth'

export interface WelcomeNewsItem {
  title: string
  url: string
}

export interface WelcomeGreeting {
  /** 旧版整句。大标题由前端按时段拼，没有 displayName 时才从这里取称呼。 */
  content: string
  featureNote: string | null
  weatherWarning: string | null
  news: WelcomeNewsItem[]
  /** 昵称优先，否则用户名。大标题里的 {name} 用这个。 */
  displayName: string
  /** 上海日历日：`10月9日 · 周五`，有实况时再接 ` · 多云 24°C`。 */
  dateLine: string
  /** 日期行上的实况，例如 `多云 24°C`。含「雨」时前端可能改用下雨问候。没有则 null。 */
  weatherBrief: string | null
  /** 今天的备忘摘要。没有则 null。 */
  memoSummary: string | null
}

export const welcomeApi = {
  getGreeting: () => unwrap<WelcomeGreeting>(http.get('/welcome/greeting')),
}
