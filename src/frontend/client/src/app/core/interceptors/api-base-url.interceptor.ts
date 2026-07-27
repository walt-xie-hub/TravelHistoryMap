import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '@env/environment';

/**
 * 基础 URL 拦截器：
 * 为以 `/` 开头的相对请求自动拼接后端 API 基础地址，
 * 从而让各业务服务只需书写相对路径（如 `/users`）。
 * 已是绝对地址（http/https）的请求原样放行。
 */
export const apiBaseUrlInterceptor: HttpInterceptorFn = (req, next) => {
  const isAbsolute = /^https?:\/\//i.test(req.url);
  if (isAbsolute) {
    return next(req);
  }

  const base = environment.apiBaseUrl.replace(/\/+$/, '');
  const path = req.url.startsWith('/') ? req.url : `/${req.url}`;
  return next(req.clone({ url: `${base}${path}` }));
};
