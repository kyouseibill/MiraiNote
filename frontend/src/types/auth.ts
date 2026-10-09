// 通用 API 响应
export interface ApiResponse<T = unknown> {
  success: boolean
  data: T | null
  message: string
}

// 用户信息
export interface User {
  id: number
  username: string
  email: string
  isAdmin: boolean
  isEmailVerified: boolean
  isActive: boolean
  lastLoginAt: string | null
  createdAt: string
}

// 登录请求
export interface LoginRequest {
  usernameOrEmail: string
  password: string
  rememberMe: boolean
}

// 注册请求
export interface RegisterRequest {
  username: string
  email: string
  password: string
  confirmPassword: string
}

// 认证 Token 响应（与后端 AuthTokenResponse 对齐）
export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  user: User
}

export type RegisterOutcome =
  | 'verification_email_sent'
  | 'verification_email_failed'
  | 'verification_disabled'

export interface RegisterResult {
  outcome: RegisterOutcome
  message: string
}

export type VerifyEmailStatus = 'verified' | 'already_verified' | 'expired' | 'invalid'

export interface VerifyEmailResult {
  status: VerifyEmailStatus
}

// 验证邮箱
export interface VerifyEmailRequest {
  token: string
}

export interface ResendVerifyEmailRequest {
  email: string
}

export interface ResendVerifyTokenRequest {
  token: string
}

export const RESEND_VERIFY_MESSAGE =
  '如果这个邮箱已注册但还没验证，几分钟内会收到验证邮件。没收到的话，请看一下垃圾邮件箱。'

// 忘记/重置密码
export interface ForgotPasswordRequest {
  email: string
}

export interface ResetPasswordRequest {
  token: string
  newPassword: string
}

// 修改密码（字段名与后端 DTO 对齐）
export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
  confirmPassword: string
}

/** 备忘手机提醒。接口不返回 key 本身。 */
export interface MemoReminderSettings {
  barkConfigured: boolean
}

export interface UpdateMemoReminderSettingsRequest {
  barkKey: string
}

/** 工作台「国家-城市」。不是密钥，接口会回显。 */
export interface WelcomeSettings {
  place: string | null
}

export interface UpdateWelcomeSettingsRequest {
  place: string
}
