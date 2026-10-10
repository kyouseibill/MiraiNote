/**
 * 欢迎语文案。每个时段只有一句，不再轮换，也不因下雨或周五换句。
 * {name} 由调用方换成 displayName（昵称，否则用户名）。
 */
export const greetingLines = {
  morning: '早上好，{name}',
  noon: '中午好，{name}',
  afternoon: '下午好，{name}',
  evening: '晚上好，{name}',
  lateNight: '夜深了，{name}，早点休息',
} as const

export type GreetingPeriod = keyof typeof greetingLines
