import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from '@core/services/auth.service';

/**
 * 公开认证接口：既不附带旧 token，也不因 401 触发登出跳转。
 * 登录/验证码/换令牌现在在 identity-service（ADR-0020），注册仍在 user-service。
 */
const PUBLIC_AUTH_ENDPOINT_PATTERN =
  /\b(auth\/(login|register|captcha)|identity\/(login|register|captcha|token|logout))$/;

/**
 * 是否为公开认证请求。匹配前先剥掉 `?query` 与 `#hash`：
 * 验证码请求带 `?t=<时间戳>` 防缓存参数，若连查询串一起匹配就会落到「需鉴权」分支，
 * 于是被附上过期 token（多一次 CORS 预检），登录页也会被 401 逻辑牵连。
 */
const isPublicAuthRequest = (url: string): boolean =>
  PUBLIC_AUTH_ENDPOINT_PATTERN.test(url.split(/[?#]/, 1)[0]);

/**
 * 认证 HTTP 拦截器：
 * - 请求阶段：为相对/绝对 API 请求自动附加 Authorization: Bearer <token>
 * - 响应阶段：401 → 先用 refresh token 静默续期并重放一次；失败才清理会话并回登录页
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.token;
  const isPublicAuth = isPublicAuthRequest(req.url);
  let request = req;
  if (token && !isPublicAuth) {
    request = req.clone({
      headers: req.headers.set('Authorization', `Bearer ${token}`),
    });
  }

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status !== 401 || isPublicAuth) {
        return throwError(() => error);
      }

      // access token 只有 20 分钟（ADR-0021）：先静默续期再重放，
      // 避免用户每 20 分钟被踢回登录页。
      return from(auth.refreshTokens()).pipe(
        switchMap((refreshed) => {
          const fresh = auth.token;
          if (!refreshed || !fresh) {
            auth.clearSession();
            void router.navigate(['/login']);
            return throwError(() => error);
          }

          return next(
            request.clone({ headers: request.headers.set('Authorization', `Bearer ${fresh}`) }),
          );
        }),
        catchError(() => {
          auth.clearSession();
          void router.navigate(['/login']);
          return throwError(() => error);
        }),
      );
    }),
  );
};
