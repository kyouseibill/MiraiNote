/** @type {import('tailwindcss').Config} */
// 设计令牌对齐视觉稿 docs/m1-ui-mockups.html（青绿主色 + AI 紫 + 纸面灰）
export default {
  content: ['./index.html', './src/**/*.{vue,ts}'],
  theme: {
    extend: {
      colors: {
        brand: {
          DEFAULT: '#0f766e',
          dark: '#0b5952',
          soft: '#f0fdfa',
          line: '#99f6e4',
        },
        ink: {
          DEFAULT: '#1f2937',
          sub: '#6b7280',
          faint: '#9ca3af',
        },
        paper: {
          DEFAULT: '#eef1f4',
          card: '#ffffff',
        },
        ai: {
          DEFAULT: '#7c3aed',
          soft: '#f5f3ff',
          line: '#ddd6fe',
        },
        warn: {
          DEFAULT: '#d97706',
          soft: '#fffbeb',
          line: '#fde8b3',
        },
      },
    },
  },
  plugins: [],
}
