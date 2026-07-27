import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotificationService } from '@core/services/notification.service';

/**
 * 全局 HTTP 错误拦截器：
 * 统一解析后端错误信息并通过通知服务提示，
 * 同时把标准化后的错误继续抛出，交由业务侧决定后续处理。
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const notification = inject(NotificationService);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      const message = resolveMessage(error);
      notification.error(message);
      return throwError(() => error);
    }),
  );
};

function resolveMessage(error: HttpErrorResponse): string {
  if (error.error instanceof ErrorEvent) {
    return `网络异常：${error.error.message}`;
  }
  if (typeof error.error === 'string' && error.error.trim()) {
    return error.error;
  }
  if (error.error?.message) {
    return error.error.message;
  }
  switch (error.status) {
    case 0:
      return '无法连接到服务器，请检查网络或后端服务。';
    case 400:
      return '请求参数有误。';
    case 401:
      return '登录状态已失效，请重新登录。';
    case 403:
      return '没有权限执行该操作。';
    case 404:
      return '请求的资源不存在。';
    case 500:
      return '服务器内部错误。';
    default:
      return `请求失败（${error.status}）。`;
  }
}
