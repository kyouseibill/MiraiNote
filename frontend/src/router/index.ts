import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    component: () => import('@/layouts/MainLayout.vue'),
    meta: { requiresAuth: true },
    children: [
      { path: '', redirect: '/dashboard' },
      {
        path: 'dashboard',
        name: 'dashboard',
        component: () => import('@/views/DashboardView.vue'),
        meta: { requiresAuth: true, title: '工作台' },
      },
      {
        path: 'work/logs',
        name: 'work-logs',
        component: () => import('@/views/WorkLogView.vue'),
        meta: { requiresAuth: true, title: '工作记录' },
      },
      {
        path: 'work/memos',
        name: 'work-memos',
        component: () => import('@/views/WorkMemoView.vue'),
        meta: { requiresAuth: true, title: '工作备忘' },
      },
      {
        path: 'life/memos',
        name: 'life-memos',
        component: () => import('@/views/LifeMemoView.vue'),
        meta: { requiresAuth: true, title: '生活备忘' },
      },
      {
        path: 'life/logs',
        name: 'life-logs',
        component: () => import('@/views/life/LifeLogView.vue'),
        meta: { requiresAuth: true, title: '生活记录' },
      },
      {
        path: 'work/reports',
        name: 'work-reports',
        component: () => import('@/views/work/WeeklyReportView.vue'),
        meta: { requiresAuth: true, title: 'AI 周报' },
      },
      {
        path: 'chat',
        name: 'chat',
        component: () => import('@/views/chat/ChatView.vue'),
        meta: { requiresAuth: true, title: 'AI 对话' },
      },
    ],
  },
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/LoginView.vue'),
    meta: { guestOnly: true, title: '登录' },
  },
  {
    path: '/register',
    name: 'register',
    component: () => import('@/views/RegisterView.vue'),
    meta: { guestOnly: true, title: '注册' },
  },
  {
    path: '/verify-email',
    name: 'verify-email',
    component: () => import('@/views/VerifyEmailView.vue'),
    meta: { title: '验证邮箱' },
  },
  {
    path: '/forgot-password',
    name: 'forgot-password',
    component: () => import('@/views/ForgotPasswordView.vue'),
    meta: { guestOnly: true, title: '忘记密码' },
  },
  {
    path: '/reset-password',
    name: 'reset-password',
    component: () => import('@/views/ResetPasswordView.vue'),
    meta: { title: '重置密码' },
  },
  { path: '/:pathMatch(.*)*', redirect: '/' },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  // 首次进入应用且未认证：尝试静默刷新
  if (!auth.isAuthenticated && !(window as any).__mn_refresh_tried) {
    ;(window as any).__mn_refresh_tried = true
    await auth.tryRefresh()
  }

  if (to.meta.title) {
    document.title = `${to.meta.title as string} · 未来ノート`
  } else {
    document.title = '未来ノート'
  }

  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (to.meta.guestOnly && auth.isAuthenticated) {
    return { name: 'dashboard' }
  }
  return true
})

export default router
