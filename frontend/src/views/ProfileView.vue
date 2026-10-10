<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { authApi } from '@/api/auth'
import { useToast } from '@/composables/useToast'
import { useResendCooldown } from '@/composables/useResendCooldown'
import { useRouter } from 'vue-router'
import { IconLogout } from '@tabler/icons-vue'
import RegionPicker from '@/components/RegionPicker.vue'
import RegionPrompt from '@/components/RegionPrompt.vue'
import { formatRegion, parseRegion } from '@/utils/region'

const auth = useAuthStore()
const toast = useToast()
const router = useRouter()

const RESET_SENT_MESSAGE = '重置邮件已发送，请到邮箱查收（也可能在垃圾箱）'
const boundEmail = computed(() => auth.user?.email?.trim() ?? '')
const maskedEmail = computed(() => maskEmail(boundEmail.value))
const resetSending = ref(false)
const { remaining: resetCooldown, start: startResetCooldown } = useResendCooldown()

function maskEmail(email: string): string {
  const at = email.lastIndexOf('@')
  if (at <= 0 || at === email.length - 1) return ''
  return `${email.slice(0, 1)}***@${email.slice(at + 1)}`
}

async function sendResetEmail() {
  const email = boundEmail.value
  if (!email || resetSending.value || resetCooldown.value > 0) return
  resetSending.value = true
  try {
    await authApi.forgotPassword({ email })
    toast.success(RESET_SENT_MESSAGE)
    startResetCooldown()
  } catch {
    // 拦截器已 toast
  } finally {
    resetSending.value = false
  }
}

function fmtDate(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('zh-CN', {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit',
  })
}

const barkKey = ref('')
const barkConfigured = ref(false)
const barkSubmitting = ref(false)
const savedPlace = ref('')
const regionPlace = ref('')
const regionSubmitting = ref(false)
const regionPromptDismissed = ref(false)
const nickname = ref('')
const nicknameSubmitting = ref(false)
const showRegionPrompt = computed(() => !savedPlace.value.trim() && !regionPromptDismissed.value)

onMounted(async () => {
  try {
    const settings = await authApi.getMemoReminderSettings()
    barkConfigured.value = settings.barkConfigured
  } catch {
    // 拦截器已 toast
  }
  try {
    applyWelcome(await authApi.getWelcomeSettings())
  } catch {
    // 拦截器已 toast
  }
})

function applyWelcome(settings: { place: string | null; nickname: string | null }) {
  savedPlace.value = settings.place ?? ''
  nickname.value = settings.nickname ?? ''
  const parsed = parseRegion(settings.place)
  regionPlace.value = parsed ? formatRegion(parsed.country, parsed.city) : ''
}

async function saveRegion() {
  regionSubmitting.value = true
  try {
    const settings = await authApi.updateWelcomeSettings({
      place: regionPlace.value.trim(),
      nickname: nickname.value.trim(),
    })
    applyWelcome(settings)
    toast.success(savedPlace.value ? '所在地区已保存' : '已关闭天气')
  } catch {
    // 拦截器已 toast
  } finally {
    regionSubmitting.value = false
  }
}

async function saveNickname() {
  nicknameSubmitting.value = true
  try {
    const settings = await authApi.updateWelcomeSettings({
      place: savedPlace.value.trim(),
      nickname: nickname.value.trim(),
    })
    applyWelcome(settings)
    toast.success(nickname.value ? '昵称已保存' : '已改回用户名')
  } catch {
    // 拦截器已 toast
  } finally {
    nicknameSubmitting.value = false
  }
}

async function saveBark() {
  barkSubmitting.value = true
  try {
    const settings = await authApi.updateMemoReminderSettings({ barkKey: barkKey.value.trim() })
    barkConfigured.value = settings.barkConfigured
    barkKey.value = ''
    toast.success(barkConfigured.value ? 'Bark key 已保存' : '已关闭手机推送')
  } catch {
    // 拦截器已 toast
  } finally {
    barkSubmitting.value = false
  }
}

async function handleLogout() {
  // 不用 window.confirm：自动化/部分浏览器会默认取消，导致退出点击无效果。
  try {
    await auth.logout()
    toast.success('已退出登录')
  } finally {
    router.replace({ name: 'login' })
  }
}
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-8 px-4 py-6 sm:px-6 lg:py-10">

    <RegionPrompt v-if="showRegionPrompt" settings @skip="regionPromptDismissed = true" />

    <!-- 账户 -->
    <section class="space-y-2.5" aria-labelledby="settings-account">
      <h2 id="settings-account" class="px-0.5 text-[11px] font-medium tracking-[0.16em] text-[#9a958d]">账户</h2>
      <div class="surface-card overflow-hidden">
        <div class="flex items-start gap-4 px-5 py-5 sm:px-6">
          <div class="flex h-16 w-16 shrink-0 select-none items-center justify-center rounded-full bg-teal-100 text-2xl font-semibold text-teal-600">
            {{ auth.user?.username?.charAt(0).toUpperCase() }}
          </div>
          <div class="min-w-0 flex-1 pt-0.5">
            <p
              class="font-serif text-[22px] font-medium leading-tight tracking-[0.04em] text-gray-900"
              data-testid="account-username"
            >
              {{ auth.user?.username }}
            </p>
            <div class="mt-2 flex flex-wrap items-center gap-x-2.5 gap-y-2">
              <span class="min-w-0 break-all text-sm text-gray-500">{{ auth.user?.email }}</span>
              <span
                class="inline-flex shrink-0 items-center rounded-full px-2.5 py-1 text-xs leading-none"
                :class="auth.user?.isEmailVerified
                  ? 'bg-green-100 text-green-700'
                  : 'bg-amber-100 text-amber-700'"
              >
                {{ auth.user?.isEmailVerified ? '邮箱已验证' : '邮箱未验证' }}
              </span>
              <span
                v-if="auth.isAdmin"
                class="inline-flex shrink-0 items-center rounded-full bg-teal-100 px-2.5 py-1 text-xs font-medium leading-none text-teal-700"
              >管理员</span>
            </div>
          </div>
        </div>

        <dl class="grid grid-cols-2 gap-x-4 gap-y-3 border-t border-gray-100 px-5 py-4 sm:gap-x-8 sm:px-6">
          <div class="min-w-0">
            <dt class="text-xs text-gray-400">用户 ID</dt>
            <dd class="mt-0.5 truncate font-mono text-[13px] text-gray-800">{{ auth.user?.id }}</dd>
          </div>
          <div class="min-w-0">
            <dt class="text-xs text-gray-400">注册时间</dt>
            <dd class="mt-0.5 text-[13px] leading-5 text-gray-800">{{ fmtDate(auth.user?.createdAt ?? null) }}</dd>
          </div>
          <div class="min-w-0">
            <dt class="text-xs text-gray-400">上次登录</dt>
            <dd class="mt-0.5 text-[13px] leading-5 text-gray-800">{{ fmtDate(auth.user?.lastLoginAt ?? null) }}</dd>
          </div>
          <div class="min-w-0">
            <dt class="text-xs text-gray-400">账户状态</dt>
            <dd class="mt-0.5">
              <span
                class="inline-flex items-center rounded-full px-2 py-0.5 text-xs leading-none"
                :class="auth.user?.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-600'"
              >
                {{ auth.user?.isActive ? '正常' : '已禁用' }}
              </span>
            </dd>
          </div>
        </dl>
      </div>
    </section>

    <!-- 个人偏好：昵称与所在地区仍各自保存，不合并提交。 -->
    <section class="space-y-2.5" aria-labelledby="settings-preferences">
      <h2 id="settings-preferences" class="px-0.5 text-[11px] font-medium tracking-[0.16em] text-[#9a958d]">个人偏好</h2>
      <div class="surface-card divide-y divide-gray-100">
        <form class="px-5 py-5 sm:px-6" data-testid="nickname-form" @submit.prevent="saveNickname">
          <label class="mb-1 block text-sm text-gray-700" for="welcome-nickname">昵称</label>
          <input
            id="welcome-nickname"
            v-model="nickname"
            type="text"
            autocomplete="off"
            maxlength="20"
            data-testid="nickname-input"
            placeholder="例如 雅美"
            class="h-9 w-full rounded-md border border-gray-200 px-3 text-sm focus:outline-none focus:ring-2 focus:ring-teal-200"
          />
          <p class="mt-1 text-xs text-gray-400" data-testid="nickname-status">
            当前：{{ nickname ? nickname : '使用用户名' }}。最多 20 个字。
          </p>
          <div class="mt-4 flex justify-end">
            <button
              type="submit"
              class="inline-flex h-9 items-center justify-center rounded-md bg-teal-600 px-5 text-sm text-white transition hover:bg-teal-700 disabled:cursor-not-allowed disabled:opacity-60"
              :disabled="nicknameSubmitting"
            >
              {{ nicknameSubmitting ? '保存中…' : '保存' }}
            </button>
          </div>
        </form>

        <form id="region" class="px-5 py-5 sm:px-6" data-testid="region-form" @submit.prevent="saveRegion">
          <p class="mb-3 text-xs leading-5 text-gray-400">先选国家，再选城市。填写后，工作台日期行会带上当天实况和气温；留空并保存则不查询、不显示。</p>
          <RegionPicker v-model="regionPlace" />
          <div class="mt-4 flex justify-end">
            <button
              type="submit"
              class="inline-flex h-9 items-center justify-center rounded-md bg-teal-600 px-5 text-sm text-white transition hover:bg-teal-700 disabled:cursor-not-allowed disabled:opacity-60"
              :disabled="regionSubmitting"
            >
              {{ regionSubmitting ? '保存中…' : '保存' }}
            </button>
          </div>
        </form>
      </div>
    </section>

    <!-- 提醒与安全 -->
    <section class="space-y-2.5" aria-labelledby="settings-safety">
      <h2 id="settings-safety" class="px-0.5 text-[11px] font-medium tracking-[0.16em] text-[#9a958d]">提醒与安全</h2>
      <div class="surface-card divide-y divide-gray-100">
        <form class="px-5 py-5 sm:px-6" data-testid="bark-form" @submit.prevent="saveBark">
          <h3 class="text-sm font-medium text-gray-900">手机提醒</h3>
          <p class="mt-1 text-xs leading-5 text-gray-400">备忘到点后推到手机。留空则不推送，邮件提醒照常。</p>
          <label class="mb-1 mt-4 block text-sm text-gray-700" for="bark-key">Bark key</label>
          <input
            id="bark-key"
            v-model="barkKey"
            type="text"
            autocomplete="off"
            spellcheck="false"
            data-testid="bark-key-input"
            placeholder="只填 key，留空表示不推送"
            class="h-9 w-full rounded-md border border-gray-200 px-3 text-sm focus:outline-none focus:ring-2 focus:ring-teal-200"
          />
          <p class="mt-1 text-xs text-gray-400">推送地址固定为 api.day.app，不用填写服务器地址。</p>
          <p class="mt-1 text-xs text-gray-400" data-testid="bark-key-status">
            当前：{{ barkConfigured ? '已填写' : '未填写' }}。留空并保存会关闭推送。
          </p>
          <div class="mt-4 flex justify-end">
            <button
              type="submit"
              class="inline-flex h-9 items-center justify-center rounded-md bg-teal-600 px-5 text-sm text-white transition hover:bg-teal-700 disabled:cursor-not-allowed disabled:opacity-60"
              :disabled="barkSubmitting"
            >
              {{ barkSubmitting ? '保存中…' : '保存' }}
            </button>
          </div>
        </form>

        <div class="px-5 py-5 sm:px-6" data-testid="login-password-section">
          <h3 class="text-sm font-medium text-gray-900">登录密码</h3>
          <p class="mt-1 text-xs leading-5 text-gray-400">通过绑定邮箱收取重置链接，邮件里设置新密码。</p>
          <div class="mt-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
            <p class="min-w-0 break-all text-sm text-gray-800" data-testid="masked-email">
              {{ maskedEmail || '未绑定邮箱' }}
            </p>
            <button
              type="button"
              class="inline-flex h-9 shrink-0 items-center justify-center rounded-md px-5 text-sm transition disabled:cursor-not-allowed"
              :class="resetCooldown > 0
                ? 'bg-gray-200 text-gray-500'
                : 'bg-teal-600 text-white hover:bg-teal-700 disabled:opacity-60'"
              data-testid="send-reset-email"
              :disabled="resetSending || resetCooldown > 0 || !boundEmail"
              @click="sendResetEmail"
            >
              <span v-if="resetSending">发送中…</span>
              <span v-else-if="resetCooldown > 0">{{ resetCooldown }} 秒后可再次发送</span>
              <span v-else>发送重置邮件</span>
            </button>
          </div>
        </div>
      </div>
    </section>

    <!-- 危险操作 -->
    <section class="space-y-2.5" aria-labelledby="settings-danger">
      <h2 id="settings-danger" class="px-0.5 text-[11px] font-medium tracking-[0.16em] text-[#9a958d]">危险操作</h2>
      <div class="surface-card px-5 py-4 sm:px-6">
        <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <p class="text-sm leading-6 text-gray-500">退出当前账号。下次使用需要重新登录。</p>
          <button
            type="button"
            class="inline-flex h-9 w-full shrink-0 items-center justify-center gap-1.5 rounded-md border border-[#ddd8cf] px-3 text-[12px] leading-none text-[#716c65] transition hover:border-[#c7a59f] hover:text-[#973a33] sm:w-auto"
            data-testid="logout-button"
            @click="handleLogout"
          >
            <IconLogout :size="15" :stroke-width="1.5" />
            退出
          </button>
        </div>
      </div>
    </section>

  </div>
</template>
