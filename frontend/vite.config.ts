import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    // Vite 会把未知路径回成 index.html。健康检查要转到 API，避免被 SPA 兜底拦住。
    proxy: {
      '/health': 'http://localhost:5273',
    },
  },
})
