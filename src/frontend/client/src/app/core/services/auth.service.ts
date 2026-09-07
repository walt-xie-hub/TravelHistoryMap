import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { lastValueFrom } from 'rxjs';
import {
  AuthResponse,
  CaptchaResponse,
  ChangePasswordRequest,
  LoginRequest,
  RegisterRequest,
  RegisterResponse,
  UpdateProfileRequest,
  User,
} from '@core/models/user.model';

const TOKEN_STORAGE_KEY = 'travel-map.auth.token';

/**
 * 认证与“当前用户”服务：
 * - token 存 localStorage（登录后 7 天内有效，由后端签发）
 * - 以 signal 维护 currentUser，供布局/页面实时读取
 * - 登出即前端清除 token；401 由全局 auth 拦截器触发 clearSession
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly _currentUser = signal<User | null>(null);
  /** 当前登录用户；未登录时为 null */
  readonly currentUser = this._currentUser.asReadonly();
  /** 是否已完成身份初始化（有本地会话且资料加载成功） */
  readonly isAuthenticated = computed(() => this._currentUser() !== null);

  private initPromise: Promise<boolean> | null = null;

  get token(): string | null {
    return localStorage.getItem(TOKEN_STORAGE_KEY);
  }

  /**
   * 应用启动 / 路由守卫时恢复会话：
   * 无本地 token 直接返回 false；有 token 则请求 users/me 校验，
   * token 失效（401）时自动清理本地会话。
   */
  async initialize(): Promise<boolean> {
    if (this._currentUser()) {
      return true;
    }
    if (!this.token) {
      return false;
    }
    this.initPromise ??= this.loadProfile()
      .then(() => true)
      .catch(() => {
        this.clearSession();
        return false;
      });
    return this.initPromise;
  }

  /** 登录：成功后保存 token 并写入 currentUser。 */
  async login(credentials: LoginRequest): Promise<User> {
    const response = await lastValueFrom(
      this.http.post<AuthResponse>('auth/login', credentials),
    );
    this.applyAuth(response);
    return response.user;
  }

  /**
   * 注册：用户信息写入数据库后返回 user，不签发 token、不建立会话。
   * 注册成功由页面引导跳转登录页，让用户携带图片验证码重新登录。
   */
  async register(payload: RegisterRequest): Promise<User> {
    const response = await lastValueFrom(
      this.http.post<RegisterResponse>('auth/register', payload),
    );
    return response.user;
  }

  /** 获取登录页图片验证码（服务端一次性凭证，5 分钟过期）。
   * 附加时间戳避免浏览器缓存旧的失败响应或图片。 */
  async getCaptcha(): Promise<CaptchaResponse> {
    return lastValueFrom(
      this.http.get<CaptchaResponse>(`auth/captcha?t=${Date.now()}`),
    );
  }

  /** 重新拉取当前用户资料并刷新 currentUser。 */
  async loadProfile(): Promise<User> {
    const user = await lastValueFrom(this.http.get<User>('users/me'));
    this._currentUser.set(user);
    return user;
  }

  /** 修改当前用户资料（姓名/邮箱/电话/头像）。 */
  async updateProfile(payload: UpdateProfileRequest): Promise<User> {
    const user = await lastValueFrom(
      this.http.put<User>('users/me', payload),
    );
    this._currentUser.set(user);
    return user;
  }

  /** 修改当前用户密码。 */
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    const body: ChangePasswordRequest = { currentPassword, newPassword };
    await lastValueFrom(this.http.put<void>('users/me/password', body));
  }

  /** 登出：清除本地 token 与内存中的用户资料。 */
  logout(): void {
    this.clearSession();
  }

  /** 清理本地会话（登出或 token 失效时调用）。 */
  clearSession(): void {
    this._currentUser.set(null);
    localStorage.removeItem(TOKEN_STORAGE_KEY);
    this.initPromise = null;
  }

  private applyAuth(response: AuthResponse): void {
    localStorage.setItem(TOKEN_STORAGE_KEY, response.token);
    this._currentUser.set(response.user);
    this.initPromise = null;
  }
}
