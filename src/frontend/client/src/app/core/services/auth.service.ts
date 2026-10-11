import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { lastValueFrom } from 'rxjs';
import {
  CaptchaResponse,
  ChangePasswordRequest,
  LoginRequest,
  RegisterRequest,
  RegisterResponse,
  UpdateProfileRequest,
  User,
} from '@core/models/user.model';
import { environment } from '@env/environment';

const TOKEN_STORAGE_KEY = 'travel-map.auth.token';
const REFRESH_TOKEN_STORAGE_KEY = 'travel-map.auth.refresh-token';

/**
 * 是否是“本地令牌已经不再有效”的失败。
 *
 * 只有 401/403 值得销毁会话；status 0（连不上）与 5xx 都是**暂时性**的 ——
 * 把两者混为一谈，就会出现“后端刚重启/网关抖一下，用户被登出，还得重新读一次验证码”。
 */
function isAuthenticationFailure(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403);
}

/**
 * identity-service 的令牌响应（ADR-0021）。刻意**不含**用户资料：
 * 档案归 user-service，登录成功后再由 users/me 单独获取（ADR-0020）。
 */
interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresInSeconds: number;
  tokenType: string;
}

/**
 * 认证与“当前用户”服务（认证在 identity-service，档案在 user-service）：
 * - access token 只有 20 分钟，401 时由全局拦截器用 refresh token 静默续期
 * - refresh token 存 localStorage（每次使用即轮换，旧令牌作废）；登出会在服务端撤销整族
 * - 以 signal 维护 currentUser，供布局/页面实时读取
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
  private refreshInFlight: Promise<boolean> | null = null;

  get token(): string | null {
    return localStorage.getItem(TOKEN_STORAGE_KEY);
  }

  /** refresh token；只在续期与登出时用得上。 */
  get refreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
  }

  /**
   * 应用启动 / 路由守卫时恢复会话：
   * 无本地 token 直接返回 false；有 token 则请求 users/me 校验。
   *
   * 只有**认证失败**（401/403）才销毁会话。user-service 没起来、网关 5xx、断网
   * 这些**暂时性**故障必须保留令牌：一次后端抖动就丢掉仍然有效的 refresh token，
   * 用户会被迫重新登录（还要再读一次验证码），而这本该只是“稍后重试”。
   * 暂时性故障后会清空 initPromise，让下一次导航/刷新还有机会重试。
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
      .catch((error: unknown) => {
        if (isAuthenticationFailure(error)) {
          this.clearSession();      // 令牌真的没用了
        } else {
          this.initPromise = null;  // 后端暂时不可用：令牌留着，下次再试
        }
        return false;
      });
    return this.initPromise;
  }

  /**
   * 登录：走 identity-service（ADR-0020），成功后保存 access + refresh，
   * 再单独拉取用户档案（档案不在令牌响应里）。
   */
  async login(credentials: LoginRequest): Promise<User> {
    const tokens = await lastValueFrom(
      this.http.post<TokenResponse>(`${environment.identityBaseUrl}/login`, credentials),
    );
    this.applyTokens(tokens);
    return this.loadProfile();
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

  /** 获取登录页图片验证码（identity-service，一次性凭证、 5 分钟过期）。
   * 附加时间戳避免浏览器缓存旧的失败响应或图片。 */
  async getCaptcha(): Promise<CaptchaResponse> {
    return lastValueFrom(
      this.http.get<CaptchaResponse>(`${environment.identityBaseUrl}/captcha?t=${Date.now()}`),
    );
  }

  /**
   * 静默续期：用 refresh token 换新令牌（每次轮换，旧令牌立即作废）。
   *
   * **单飞**：并发的 401 共用同一次刷新。否则第二个请求会拿着已被轮换的 refresh token
   * 再换一次，而服务端把“旧令牌被再次使用”判定为泄露 → 整族撤销，用户直接被登出。
   */
  async refreshTokens(): Promise<boolean> {
    this.refreshInFlight ??= this.performRefresh().finally(() => {
      this.refreshInFlight = null;
    });
    return this.refreshInFlight;
  }

  private async performRefresh(): Promise<boolean> {
    const refreshToken = this.refreshToken;
    if (!refreshToken) {
      return false;
    }

    try {
      const body = new HttpParams()
        .set('grant_type', 'refresh_token')
        .set('refresh_token', refreshToken);
      const tokens = await lastValueFrom(
        this.http.post<TokenResponse>(`${environment.identityBaseUrl}/token`, body.toString(), {
          headers: new HttpHeaders({ 'Content-Type': 'application/x-www-form-urlencoded' }),
        }),
      );
      this.applyTokens(tokens);
      return true;
    } catch {
      // 令牌已被轮换过/已撤销/账号已停用——都不能自己恢复
      return false;
    }
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

  /** 登出：先在服务端撤销该 refresh 所在的整族，再清本地。
   *  网络失败也照样清本地——本地令牌本来就会在 20 分钟后过期。 */
  async logout(): Promise<void> {
    const refreshToken = this.refreshToken;
    this.clearSession();

    if (!refreshToken) {
      return;
    }

    try {
      await lastValueFrom(
        this.http.post(`${environment.identityBaseUrl}/logout`, { refreshToken }),
      );
    } catch {
      // 已登出，无需向调用方报错
    }
  }

  /** 清理本地会话（登出或续期失败时调用）。 */
  clearSession(): void {
    this._currentUser.set(null);
    localStorage.removeItem(TOKEN_STORAGE_KEY);
    localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
    this.initPromise = null;
  }

  private applyTokens(response: TokenResponse): void {
    localStorage.setItem(TOKEN_STORAGE_KEY, response.accessToken);
    localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, response.refreshToken);
    this.initPromise = null;
  }
}
