<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { IconLoader2 } from '@tabler/icons-vue'
import { useHouseholdFeedback } from '@/composables/useHouseholdFeedback'
import type { HouseholdNotificationChannel, HouseholdNotificationSettings } from '@/types/household'
import { barkAddressError, canonicalHttpsUrl } from '@/utils/householdFormat'

const { toast, store, report } = useHouseholdFeedback()
const loading = ref(true)
const saving = ref(false)
const testingBark = ref(false)
const testingEmail = ref(false)
const pageError = ref('')
const barkAddress = ref('')
const barkEnabled = ref(true)
const emailEnabled = ref(true)
const pushTime = ref('09:00')
const leadChannel = ref<HouseholdNotificationChannel>('Email')
const dueChannel = ref<HouseholdNotificationChannel>('Bark')
const overdueIntervalDays = ref('3')
const clearBark = ref(false)

const settings = computed(() => store.notificationSettings)
const barkHint = computed(() => {
  if (clearBark.value) return '保存后会清除已保存的 Bark 地址'
  if (!settings.value?.barkConfigured) return 'Bark 地址等同密钥，保存后只显示末 4 位'
  const suffix = settings.value.barkAddressSuffix
  return suffix ? `已配置 ····${suffix}。留空则不修改` : '已配置。留空则不修改'
})

function apply(next: HouseholdNotificationSettings) {
  barkEnabled.value = next.barkEnabled
  emailEnabled.value = next.emailEnabled
  pushTime.value = `${String(next.pushHour).padStart(2, '0')}:${String(next.pushMinute).padStart(2, '0')}`
  leadChannel.value = next.leadChannel
  dueChannel.value = next.dueChannel
  overdueIntervalDays.value = String(next.overdueIntervalDays)
  barkAddress.value = ''
  clearBark.value = false
}

async function load() {
  loading.value = true
  pageError.value = ''
  try {
    apply(await store.fetchNotificationSettings())
  } catch (error) {
    pageError.value = (await report(error)).message
  } finally {
    loading.value = false
  }
}

onMounted(load)

function parseTime(value: string) {
  const match = /^(\d{2}):(\d{2})$/.exec(value)
  if (!match) return null
  const hour = Number(match[1])
  const minute = Number(match[2])
  if (hour > 23 || minute > 59) return null
  return { hour, minute }
}

function validate(forTest: 'bark' | 'email' | null) {
  const time = parseTime(pushTime.value)
  if (!time) return '请填写每天的推送时间'
  if (!/^\d+$/.test(overdueIntervalDays.value) || Number(overdueIntervalDays.value) < 1 || Number(overdueIntervalDays.value) > 365) {
    return '逾期重复间隔需在 1 到 365 天之间'
  }
  if (barkAddress.value.trim() && !canonicalHttpsUrl(barkAddress.value)) return barkAddressError
  if (forTest === 'bark' && !barkAddress.value.trim() && !settings.value?.barkConfigured) return '请先填写 Bark 地址'
  if (forTest === 'email' && !settings.value?.email) return '账号没有邮箱'
  return ''
}

async function save() {
  const problem = validate(null)
  if (problem) {
    toast.error(problem)
    return
  }
  const time = parseTime(pushTime.value)!
  saving.value = true
  try {
    const saved = await store.saveNotificationSettings({
      barkEnabled: barkEnabled.value,
      barkAddress: barkAddress.value.trim() || null,
      clearBarkAddress: clearBark.value,
      emailEnabled: emailEnabled.value,
      email: settings.value?.email ?? null,
      pushHour: time.hour,
      pushMinute: time.minute,
      leadChannel: leadChannel.value,
      dueChannel: dueChannel.value,
      overdueIntervalDays: Number(overdueIntervalDays.value),
    })
    apply(saved)
    toast.success(store.previewMode ? '设计预览已记下这些设置' : '已保存')
  } catch (error) {
    await report(error)
  } finally {
    saving.value = false
  }
}

async function sendTest(kind: 'bark' | 'email') {
  const problem = validate(kind)
  if (problem) {
    toast.error(problem)
    return
  }
  if (store.previewMode) {
    toast.info('设计预览不会真正发送')
    return
  }
  const flag = kind === 'bark' ? testingBark : testingEmail
  flag.value = true
  try {
    await store.testNotificationChannel(kind, kind === 'bark' ? barkAddress.value.trim() : settings.value?.email)
    toast.success('测试通知已发送')
  } catch (error) {
    await report(error)
  } finally {
    flag.value = false
  }
}
</script>

<template>
  <div>
    <p class="mb-4 max-w-2xl text-[13px] leading-6 text-[#68665f]">
      只保存你自己的通知。Bark 地址只接受 https，保存后不再显示完整内容。邮件只会发到账号邮箱，不能改成其他地址。单个事项的提前提醒天数在事项里改，默认 7 天。
    </p>
    <p v-if="settings && !settings.notificationsEnabled" class="mb-4 rounded-md border border-[#e4d3a8] bg-[#fffaf0] px-4 py-3 text-[13px] leading-6 text-[#6d5a2d]">
      提醒功能尚未在服务器开启。可以先保存设置、发送测试；到点不会自动推送。
    </p>
    <p v-if="pageError" role="alert" class="mb-4 rounded-md border border-[#e4bbb7] bg-[#fff5f3] px-4 py-3 text-[13px] text-[#9d3b34]">
      {{ pageError }}
      <button type="button" class="ml-3 text-[#4c6178] hover:underline" @click="load">重试</button>
    </p>
    <div v-if="loading" class="flex h-32 items-center justify-center text-[13px] text-[var(--mn-muted)]">
      <IconLoader2 :size="18" class="mr-2 animate-spin" />正在读取通知设置
    </div>
    <form v-else class="max-w-xl space-y-5" @submit.prevent="save">
      <fieldset class="space-y-3 rounded-md border border-[var(--mn-line)] px-4 py-4">
        <legend class="px-1 text-[13px] font-medium">Bark</legend>
        <label class="flex items-center gap-2 text-[13px]">
          <input v-model="barkEnabled" type="checkbox" :disabled="saving" />
          开启 Bark
        </label>
        <div>
          <label class="text-[13px] font-medium" for="notify-bark">Bark 地址</label>
          <input id="notify-bark" v-model="barkAddress" type="password" autocomplete="off" class="form-input mt-1.5 h-10" :placeholder="settings?.barkConfigured ? '留空则不修改' : 'https://'" :disabled="saving || clearBark" />
          <p class="mt-1 text-[12px] text-[var(--mn-muted)]">{{ barkHint }}</p>
        </div>
        <div class="flex flex-wrap gap-2">
          <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-3 text-[13px]" :disabled="saving || testingBark" @click="sendTest('bark')">
            {{ testingBark ? '发送中…' : '发送测试' }}
          </button>
          <button v-if="settings?.barkConfigured" type="button" class="h-9 px-3 text-[13px] text-[#b4493f]" :disabled="saving" @click="clearBark = !clearBark">
            {{ clearBark ? '取消清除' : '清除地址' }}
          </button>
        </div>
      </fieldset>

      <fieldset class="space-y-3 rounded-md border border-[var(--mn-line)] px-4 py-4">
        <legend class="px-1 text-[13px] font-medium">邮件</legend>
        <label class="flex items-center gap-2 text-[13px]">
          <input v-model="emailEnabled" type="checkbox" :disabled="saving" />
          开启邮件
        </label>
        <div>
          <label class="text-[13px] font-medium" for="notify-email">收件邮箱</label>
          <input id="notify-email" :value="settings?.email || ''" type="email" readonly autocomplete="off" class="form-input mt-1.5 h-10" />
          <p class="mt-1 text-[12px] text-[var(--mn-muted)]">只会发到这个账号邮箱。要换邮箱，去改账号本身。</p>
        </div>
        <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-3 text-[13px]" :disabled="saving || testingEmail" @click="sendTest('email')">
          {{ testingEmail ? '发送中…' : '发送测试' }}
        </button>
      </fieldset>

      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label class="text-[13px] font-medium" for="notify-time">每天推送时间（北京时间）</label>
          <input id="notify-time" v-model="pushTime" type="time" class="form-input mt-1.5 h-10" :disabled="saving" />
        </div>
        <div>
          <label class="text-[13px] font-medium" for="notify-interval">逾期重复间隔（天）</label>
          <input id="notify-interval" v-model="overdueIntervalDays" type="number" min="1" max="365" step="1" class="form-input mt-1.5 h-10" :disabled="saving" />
        </div>
        <div>
          <label class="text-[13px] font-medium" for="notify-lead">提前 N 天</label>
          <select id="notify-lead" v-model="leadChannel" class="form-input mt-1.5 h-10" :disabled="saving">
            <option value="Email">邮件</option>
            <option value="Bark">Bark</option>
          </select>
        </div>
        <div>
          <label class="text-[13px] font-medium" for="notify-due">到期当天和逾期</label>
          <select id="notify-due" v-model="dueChannel" class="form-input mt-1.5 h-10" :disabled="saving">
            <option value="Email">邮件</option>
            <option value="Bark">Bark</option>
          </select>
        </div>
      </div>
      <p class="text-[12px] leading-6 text-[var(--mn-muted)]">默认提前 N 天发邮件，到期当天和逾期推 Bark，每天 09:00，逾期每 3 天一次。改推送时间后，当天已发的不会重发，还没发的按新时间发。</p>
      <button type="submit" class="h-10 rounded-md bg-[var(--mn-indigo)] px-4 text-[13px] font-medium text-white disabled:opacity-50" :disabled="saving">
        {{ saving ? '保存中…' : '保存通知设置' }}
      </button>
    </form>
  </div>
</template>
