import { onUnmounted, ref } from 'vue'

export function useResendCooldown(seconds = 60) {
  const remaining = ref(0)
  let timer: ReturnType<typeof setInterval> | undefined

  function start() {
    remaining.value = seconds
    if (timer) clearInterval(timer)
    timer = setInterval(() => {
      remaining.value -= 1
      if (remaining.value <= 0) {
        remaining.value = 0
        if (timer) clearInterval(timer)
        timer = undefined
      }
    }, 1000)
  }

  onUnmounted(() => {
    if (timer) clearInterval(timer)
  })

  return { remaining, start }
}
