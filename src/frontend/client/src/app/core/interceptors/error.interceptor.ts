import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotificationService } from '@core/services/notification.service';
import { extractErrorMessage } from '@core/utils/error-message.util';

/**
 * 全局 HTTP 错误拦截器：
 * 统一解析后端错误信息并通过通知服务提示，
 * 同时把标准化后的错误继续抛出，交由业务侧决定后续处理。
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const notification = inject(NotificationService);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      notification.error(extractErrorMessage(error));
      return throwError(() => error);
    }),
  );
};
