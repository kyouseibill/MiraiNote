<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useToast } from '@/composables/useToast'
import AuthCard from '@/components/AuthCard.vue'
import FormField from '@/components/FormField.vue'
import PasswordInput from '@/components/PasswordInput.vue'

const auth = useAuthStore()
const router = useRouter()
const toast = useToast()

const form = reactive({
  username: '',
  email: '',
  password: '',
  confirmPassword: '',
})
// 用于区分各字段错误（空字符串 = 无错误）
const errors = reactive({
  username: '',
  email: '',
  password: '',
  confirmPassword: '',
})
const loading = ref(false)

// 用户名：字母或数字开头；中间可含字母、数字、下划线、点；
// 相邻两个特殊字符之间必须有字母/数字；不能以特殊字符结尾；3-30 位
const usernameFormatRe = /^[A-Za-z0-9]([A-Za-z0-9]|[._][A-Za-z0-9])*$/
const emailRe = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const passwordRe = /^(?=.*[A-Za-z])(?=.*\d).{8,32}$/

// 实时监听密码一致性：flush:'sync' 保证每次响应式状态变化后立即同步，不会有异步延迟
watch([() => form.password, () => form.confirmPassword], ([pwd, cpwd]) => {
  if (cpwd) {
    errors.confirmPassword = cpwd === pwd ? '' : '两次输入的密码不一致'
  } else {
    errors.confirmPassword = ''
  }
}, { flush: 'sync' })

// 实时监听密码格式，正确后自动清除错误
watch(() => form.password, (v) => {
  if (errors.password && passwordRe.test(v)) errors.password = ''
})

// 实时监听用户名格式
watch(() => form.username, (v) => {
  if (errors.username && v.length >= 3 && v.length <= 30 && usernameFormatRe.test(v)) {
    errors.username = ''
  }
})

function checkUsername() {
  if (!form.username) return
  const len = form.username.length
  if (len < 3 || len > 30) {
    errors.username = '用户名长度为 3–30 个字符'
  } else if (!usernameFormatRe.test(form.username)) {
    errors.username = '只能含字母、数字、下划线（_）或点（.），不可连续使用特殊字符，且不能以特殊字符结尾'
  } else {
    errors.username = ''
  }
}

function checkEmail() {
  if (!form.email) return
  errors.email = emailRe.test(form.email) ? '' : '邮箱格式不正确'
}

function checkPassword() {
  if (!form.password) return
  errors.password = passwordRe.test(form.password) ? '' : '密码 8–32 位，须包含字母与数字'
  // 密码变动时，若确认密码字段有内容，联动重算
  if (form.confirmPassword) {
    errors.confirmPassword = form.confirmPassword === form.password ? '' : '两次输入的密码不一致'
  }
}

function checkConfirmPassword() {
  if (!form.confirmPassword) return
  errors.confirmPassword = form.confirmPassword === form.password ? '' : '两次输入的密码不一致'
}

// ---- 提交前全量校验（空字段也须报错）----

function validate(): boolean {
  const uLen = form.username.length
  if (uLen === 0) {
    errors.username = '请输入用户名'
  } else if (uLen < 3 || uLen > 30) {
    errors.username = '用户名长度为 3–30 个字符'
  } else if (!usernameFormatRe.test(form.username)) {
    errors.username = '只能含字母、数字、下划线（_）或点（.），不可连续使用特殊字符，且不能以特殊字符结尾'
  } else {
    errors.username = ''
  }

  errors.email = !form.email ? '请输入邮箱' : emailRe.test(form.email) ? '' : '邮箱格式不正确'

  errors.password = !form.password ? '请输入密码' : passwordRe.test(form.password) ? '' : '密码 8–32 位，须包含字母与数字'

  errors.confirmPassword = !form.confirmPassword
    ? '请再次输入密码'
    : form.confirmPassword === form.password
      ? ''
      : '两次输入的密码不一致'

  return !errors.username && !errors.email && !errors.password && !errors.confirmPassword
}

async function onSubmit() {
  if (!validate()) return
  loading.value = true
  try {
    await auth.register({
      username: form.username,
      email: form.email,
      password: form.password,
      confirmPassword: form.confirmPassword,
    })
    toast.success('注册成功，请登录')
    router.replace({ name: 'login', query: { username: form.username } })
  } catch {
    // toast 已显示
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <AuthCard title="创建未来ノート账号" subtitle="开启你的个人助理之旅">
    <form class="space-y-4" @submit.prevent="onSubmit">
      <FormField label="用户名" :error="errors.username" hint="3–30 位，以字母或数字开头，可含字母、数字、下划线（_）、点（.），不允许连续出现特殊字符">
        <input v-model="form.username" type="text" autocomplete="username" class="form-input" placeholder="例如 mirai.user 或 john_doe" @blur="checkUsername" />
      </FormField>

      <FormField label="邮箱" :error="errors.email">
        <input v-model="form.email" type="email" autocomplete="email" class="form-input" placeholder="you@example.com" @blur="checkEmail" />
      </FormField>

      <FormField label="密码" :error="errors.password" hint="8-32 位，须包含字母与数字">
        <PasswordInput v-model="form.password" autocomplete="new-password" @blur="checkPassword" />
      </FormField>

      <FormField label="确认密码" :error="errors.confirmPassword">
        <PasswordInput v-model="form.confirmPassword" autocomplete="new-password" @blur="checkConfirmPassword" />
      </FormField>

      <button type="submit" class="btn-primary" :disabled="loading">
        <span v-if="loading">注册中…</span>
        <span v-else>注册</span>
      </button>

      <p class="text-sm text-center text-gray-600">
        已有账号？
        <router-link to="/login" class="text-brand hover:text-brand-dark font-medium">立即登录</router-link>
      </p>
    </form>
  </AuthCard>
</template>
