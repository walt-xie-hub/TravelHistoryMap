/** 当前登录用户（与 user-service users/me 返回结构一致）。 */
export interface User {
  id: string;
  name: string;
  email: string;
  phoneNumber: string | null;
  avatarUrl: string | null;
}

export interface RegisterRequest {
  name: string;
  email: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
  /** 图片验证码凭证：先 GET auth/captcha 获取，再随登录提交（一次性、5 分钟过期）。 */
  captchaId?: string | null;
  captchaAnswer?: string | null;
}

export interface AuthResponse {
  token: string;
  user: User;
}

/** 注册成功响应：后端只落库并返回用户信息，不签发 token（用户需跳转登录页重新登录）。 */
export interface RegisterResponse {
  user: User;
}

/** GET auth/captcha 响应：captchaImage 为可直接放入 <img src> 的 data URI。 */
export interface CaptchaResponse {
  captchaId: string;
  captchaImage: string;
}

export interface UpdateProfileRequest {
  name: string;
  email: string;
  phoneNumber?: string | null;
  avatarUrl?: string | null;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}
