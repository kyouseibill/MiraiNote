import { createRouter, createWebHistory } from 'vue-router'

// M1 路由表（契约 §5.3，视觉稿 docs/m1-ui-mockups.html）
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue'), meta: { public: true } },
    { path: '/', name: 'today', component: () => import('@/views/TodayView.vue') },
    { path: '/inbox', name: 'inbox', component: () => import('@/views/InboxView.vue') },
    { path: '/work', name: 'work', component: () => import('@/views/WorkView.vue') },
    { path: '/life', name: 'life', component: () => import('@/views/LifeView.vue') },
    { path: '/tasks', name: 'tasks', component: () => import('@/views/TasksView.vue') },
    { path: '/reports', name: 'reports', component: () => import('@/views/ReportsView.vue') },
    { path: '/okr', name: 'okr', component: () => import('@/views/OkrView.vue') },
    { path: '/memory', name: 'memory', component: () => import('@/views/MemoryView.vue') },
    { path: '/settings', name: 'settings', component: () => import('@/views/SettingsView.vue') },
    { path: '/capture', name: 'capture', component: () => import('@/capture/CaptureWindow.vue'), meta: { public: true } },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

router.beforeEach((to) => {
  // 直读 localStorage：登录态必须每次导航都取新值（Pinia getter 只读 localStorage 会因
  // 无响应式依赖被 computed 缓存旧值，导致登录成功后 push 被守卫静默弹回——联调实测）。
  const authenticated = !!localStorage.getItem('mirai.accessToken')
  if (!to.meta.public && !authenticated) return { name: 'login' }
  if (to.name === 'login' && authenticated) return { name: 'today' }
})

export default router
