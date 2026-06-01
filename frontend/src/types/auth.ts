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

// 验证邮箱
export interface VerifyEmailRequest {
  token: string
}

export interface ResendVerifyEmailRequest {
  email: string
}

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
