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
  <div class="max-w-3xl mx-auto px-4 py-6 sm:px-6 lg:py-10 space-y-8">

    <RegionPrompt v-if="showRegionPrompt" settings @skip="regionPromptDismissed = true" />

    <!-- 账户信息卡 -->
    <section class="surface-card overflow-hidden">
      <div class="px-6 py-4 border-b border-gray-100 flex items-center gap-3">
        <!-- 头像占位 -->
        <div class="w-12 h-12 rounded-full bg-teal-100 text-teal-600 flex items-center justify-center text-xl font-bold select-none">
          {{ auth.user?.username?.charAt(0).toUpperCase() }}
        </div>
        <div>
          <p class="font-semibold text-gray-900 text-lg">{{ auth.user?.username }}</p>
          <p class="text-sm text-gray-500">{{ auth.user?.email }}</p>
        </div>
        <div class="ml-auto flex gap-2">
          <span
            v-if="auth.isAdmin"
            class="text-xs px-2 py-0.5 rounded-full bg-teal-100 text-teal-700 font-medium"
          >管理员</span>
          <span
            class="inline-flex items-center leading-none text-xs px-2.5 py-1 rounded-full"
            :class="auth.user?.isEmailVerified
              ? 'bg-green-100 text-green-700'
              : 'bg-amber-100 text-amber-700'"
          >
            {{ auth.user?.isEmailVerified ? '邮箱已验证' : '邮箱未验证' }}
          </span>
          <button
            class="ml-2 inline-flex h-8 items-center gap-1.5 rounded-md border border-[#ddd8cf] px-3 text-[12px] leading-none text-[#716c65] transition hover:border-[#c7a59f] hover:text-[#973a33]"
            data-testid="logout-button" @click="handleLogout"
          >
            <IconLogout :size="15" :stroke-width="1.5" />
            退出
          </button>
        </div>
      </div>

      <dl class="divide-y divide-gray-50 px-6">
        <div class="py-3 flex items-center justify-between">
          <dt class="text-sm text-gray-500">用户 ID</dt>
          <dd class="text-sm text-gray-800 font-mono">{{ auth.user?.id }}</dd>
        </div>
        <div class="py-3 flex items-center justify-between">
          <dt class="text-sm text-gray-500">账户状态</dt>
          <dd class="text-sm">
            <span
              class="px-2 py-0.5 rounded-full text-xs"
              :class="auth.user?.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-600'"
            >
              {{ auth.user?.isActive ? '正常' : '已禁用' }}
            </span>
          </dd>
        </div>
        <div class="py-3 flex items-center justify-between">
          <dt class="text-sm text-gray-500">注册时间</dt>
          <dd class="text-sm text-gray-800">{{ fmtDate(auth.user?.createdAt ?? null) }}</dd>
        </div>
        <div class="py-3 flex items-center justify-between">
          <dt class="text-sm text-gray-500">上次登录</dt>
          <dd class="text-sm text-gray-800">{{ fmtDate(auth.user?.lastLoginAt ?? null) }}</dd>
        </div>
      </dl>
    </section>

    <!-- 手机提醒 -->
    <section class="surface-card">
      <div class="px-6 py-4 border-b border-gray-100">
        <h2 class="font-semibold text-gray-900">手机提醒</h2>
        <p class="text-sm text-gray-500 mt-0.5">备忘到点后推到手机。留空则不推送，邮件提醒照常。</p>
      </div>
      <form class="px-6 py-5 space-y-3" @submit.prevent="saveBark">
        <div>
          <label class="block text-sm text-gray-700 mb-1" for="bark-key">Bark key</label>
          <input
            id="bark-key"
            v-model="barkKey"
            type="text"
            autocomplete="off"
            spellcheck="false"
            data-testid="bark-key-input"
            placeholder="只填 key，留空表示不推送"
            class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-200"
          />
          <p class="mt-1 text-xs text-gray-400">推送地址固定为 api.day.app，不用填写服务器地址。</p>
          <p class="mt-1 text-xs text-gray-500" data-testid="bark-key-status">
            当前：{{ barkConfigured ? '已填写' : '未填写' }}。留空并保存会关闭推送。
          </p>
        </div>
        <button
          type="submit"
          class="h-9 px-5 rounded-md bg-teal-600 text-white text-sm hover:bg-teal-700 disabled:opacity-60 transition"
          :disabled="barkSubmitting"
        >
          {{ barkSubmitting ? '保存中…' : '保存' }}
        </button>
      </form>
    </section>

    <!-- 欢迎语称呼。只影响工作台大标题，账户名仍是用户名。 -->
    <section class="surface-card">
      <div class="px-6 py-4 border-b border-gray-100">
        <h2 class="font-semibold text-gray-900">欢迎语称呼</h2>
        <p class="text-sm text-gray-500 mt-0.5">只改工作台大标题。留空则继续用用户名，其他页面不变。</p>
      </div>
      <form class="px-6 py-5 space-y-3" data-testid="nickname-form" @submit.prevent="saveNickname">
        <div>
          <label class="block text-sm text-gray-700 mb-1" for="welcome-nickname">昵称</label>
          <input
            id="welcome-nickname"
            v-model="nickname"
            type="text"
            autocomplete="off"
            maxlength="20"
            data-testid="nickname-input"
            placeholder="例如 雅美"
            class="w-full h-9 px-3 rounded-md border border-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-200"
          />
          <p class="mt-1 text-xs text-gray-500" data-testid="nickname-status">
            当前：{{ nickname ? nickname : '使用用户名' }}。最多 20 个字。
          </p>
        </div>
        <button
          type="submit"
          class="h-9 px-5 rounded-md bg-teal-600 text-white text-sm hover:bg-teal-700 disabled:opacity-60 transition"
          :disabled="nicknameSubmitting"
        >
          {{ nicknameSubmitting ? '保存中…' : '保存' }}
        </button>
      </form>
    </section>

    <!-- 所在地区。和注册页共用选择器，只保留国家、城市两级。 -->
    <section id="region" class="surface-card">
      <div class="px-6 py-4 border-b border-gray-100">
        <h2 class="font-semibold text-gray-900">所在地区</h2>
        <p class="text-sm text-gray-500 mt-0.5">先选国家，再选城市。填写后，工作台日期行会带上当天实况和气温；留空并保存则不查询、不显示。</p>
      </div>
      <form class="px-6 py-5 space-y-3" data-testid="region-form" @submit.prevent="saveRegion">
        <RegionPicker v-model="regionPlace" />
        <button
          type="submit"
          class="h-9 px-5 rounded-md bg-teal-600 text-white text-sm hover:bg-teal-700 disabled:opacity-60 transition"
          :disabled="regionSubmitting"
        >
          {{ regionSubmitting ? '保存中…' : '保存' }}
        </button>
      </form>
    </section>

    <!-- 登录密码：只发重置邮件，不在页面上填写新密码。 -->
    <section class="surface-card" data-testid="login-password-section">
      <div class="px-6 py-4 border-b border-gray-100">
        <h2 class="font-semibold text-gray-900">登录密码</h2>
        <p class="text-sm text-gray-500 mt-0.5">通过绑定邮箱收取重置链接，邮件里设置新密码。</p>
      </div>
      <div class="px-6 py-5 flex flex-wrap items-center justify-between gap-3">
        <p class="text-sm text-gray-800" data-testid="masked-email">
          {{ maskedEmail || '未绑定邮箱' }}
        </p>
        <button
          type="button"
          class="h-9 px-5 rounded-md bg-teal-600 text-white text-sm hover:bg-teal-700 disabled:opacity-60 transition"
          data-testid="send-reset-email"
          :disabled="resetSending || resetCooldown > 0 || !boundEmail"
          @click="sendResetEmail"
        >
          <span v-if="resetSending">发送中…</span>
          <span v-else-if="resetCooldown > 0">{{ resetCooldown }} 秒后可再次发送</span>
          <span v-else>发送重置邮件</span>
        </button>
      </div>
    </section>

  </div>
</template>
