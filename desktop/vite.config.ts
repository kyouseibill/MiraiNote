import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

// 端口避开 frontend 的 5173；envPrefix 允许 MIRAI_ 前缀变量（契约约定的 MIRAI_API_BASE）
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
 server: {
    port: 5174,
    strictPort: true,
  },
  envPrefix: ['MIRAI_', 'VITE_'],
})
