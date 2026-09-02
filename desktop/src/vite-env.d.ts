/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly MIRAI_API_BASE?: string
  readonly MIRAI_USE_MOCK?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
