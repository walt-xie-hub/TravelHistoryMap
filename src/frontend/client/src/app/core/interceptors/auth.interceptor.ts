import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '@core/services/auth.service';

/** 登录/注册/验证码是公开接口，不应也不需要带上已有 token。 */
const PUBLIC_AUTH_ENDPOINT_PATTERN = /\bauth\/(login|register|captcha)$/;

/**
 * 认证 HTTP 拦截器：
 * - 请求阶段：为相对/绝对 API 请求自动附加 Authorization: Bearer <token>
 * - 响应阶段：401 且非公开认证接口 → 清理本地会话并跳回登录页
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.token;
  let request = req;
  if (token && !PUBLIC_AUTH_ENDPOINT_PATTERN.test(req.url)) {
    request = req.clone({
      headers: req.headers.set('Authorization', `Bearer ${token}`),
    });
  }

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !PUBLIC_AUTH_ENDPOINT_PATTERN.test(req.url)) {
        auth.clearSession();
        void router.navigate(['/login']);
      }
      return throwError(() => error);
    }),
  );
};
