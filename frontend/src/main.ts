import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import './assets/main.css'

const app = createApp(App)
app.use(createPinia())
app.use(router)

// 全局错误处理：捕获组件渲染错误，防止整页白屏
app.config.errorHandler = (err, _instance, info) => {
  console.error('[Vue Error]', info, err)
}

app.mount('#app')
