import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '@core/services/auth.service';

/** 登录/注册接口自身的失败（401/409）属于正常业务结果，不做会话清理与跳转。 */
const AUTH_ENDPOINT_PATTERN = /\/api\/auth\/(login|register)$/;

/**
 * 认证 HTTP 拦截器：
 * - 请求阶段：为相对/绝对 API 请求自动附加 Authorization: Bearer <token>
 * - 响应阶段：401 且非登录/注册接口 → 清理本地会话并跳回登录页
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.token;
  let request = req;
  if (token) {
    request = req.clone({
      headers: req.headers.set('Authorization', `Bearer ${token}`),
    });
  }

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !AUTH_ENDPOINT_PATTERN.test(req.url)) {
        auth.clearSession();
        void router.navigate(['/login']);
      }
      return throwError(() => error);
    }),
  );
};
