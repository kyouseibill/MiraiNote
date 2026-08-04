/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{vue,ts,tsx,js,jsx}'],
  theme: {
    extend: {
      colors: {
        brand: {
          DEFAULT: '#4c6178',
          dark: '#384b60',
          soft: '#edf0f2',
        },
        paper: {
          DEFAULT: '#f6f3ec',
          light: '#fcfbf8',
          line: '#ddd8cf',
        },
        vermilion: {
          DEFAULT: '#b4493f',
          dark: '#973a33',
          soft: '#f8ece9',
        },
        teal: {
          50: '#f1f4f6', 100: '#e3e8ec', 200: '#c8d1d8', 300: '#a5b4c0',
          400: '#7b8fa0', 500: '#617789', 600: '#4c6178', 700: '#3f5267',
          800: '#384756', 900: '#313d49', 950: '#202832',
        },
        rose: {
          50: '#faf1ef', 100: '#f4e1dd', 200: '#e9c6bf', 300: '#dba49a',
          400: '#c77a6c', 500: '#b85c45', 600: '#a34b3a', 700: '#883c31',
          800: '#71342c', 900: '#5f302a', 950: '#341714',
        },
      },
      fontFamily: {
        sans: ['"Noto Sans SC"', '"PingFang SC"', '"Microsoft YaHei"', 'sans-serif'],
        serif: ['"Noto Serif SC"', '"Songti SC"', 'SimSun', 'serif'],
      },
      boxShadow: {
        panel: '0 1px 2px rgba(51, 47, 42, 0.03)',
        float: '0 18px 45px rgba(51, 47, 42, 0.12)',
      },
    },
  },
  plugins: [require('@tailwindcss/typography')],
}

