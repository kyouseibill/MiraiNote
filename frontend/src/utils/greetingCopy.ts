/**
 * 欢迎语文案。改句子只动这个文件。
 * {name} 由调用方换成 displayName（昵称，否则用户名）。
 */
export const greetingPools = {
  morning: [
    '早安，{name}',
    '早上好，{name}',
    '新的一天，{name}',
    '{name}，早',
  ],
  noon: [
    '中午好，{name}',
    '{name}，记得吃饭',
    '午安，{name}',
    '{name}，歇一会儿吧',
  ],
  afternoon: [
    '下午好，{name}',
    '{name}，喝杯茶？',
    '下午也加油，{name}',
    '{name}，下午好呀',
  ],
  evening: [
    '晚上好，{name}',
    '{name}，今天辛苦了',
    '晚上好，{name}，放松一下',
    '{name}，晚上好呀',
  ],
  lateNight: [
    '夜深了，{name}',
    '{name}，早点休息',
    '还没睡呀，{name}',
    '夜深了，{name}，别熬太晚',
  ],
} as const

export type GreetingPeriod = keyof typeof greetingPools

/** weatherBrief 含「雨」时用。深夜不用。 */
export const rainGreeting = '下雨了，{name}，记得带伞'

/** 本地日期为周五时用。下雨和深夜都优先于这句。 */
export const fridayGreeting = '周五了，{name}'
