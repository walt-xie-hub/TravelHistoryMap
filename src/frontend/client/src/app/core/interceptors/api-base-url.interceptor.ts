import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '@env/environment';

/**
 * 基础 URL 拦截器：
 * 为以 `/` 开头的相对请求自动拼接后端 API 基础地址，
 * 从而让各业务服务只需书写相对路径（如 `/users`）。
 * 已是绝对地址（http/https）的请求原样放行。
 *
 * 幂等性：若请求路径已带 API 基础前缀（如 travel 服务的 travelApiBaseUrl
 * 在生产与 apiBaseUrl 同为 `/api`，其请求为 `/api/travels`），则不再重复拼接，
 * 避免产生 `/api/api/travels` 这类错误路径。
 */
export const apiBaseUrlInterceptor: HttpInterceptorFn = (req, next) => {
  const isAbsolute = /^https?:\/\//i.test(req.url);
  if (isAbsolute) {
    return next(req);
  }

  const base = environment.apiBaseUrl.replace(/\/+$/, '');
  const path = req.url.startsWith('/') ? req.url : `/${req.url}`;

  // 路径已以 API 基础前缀开头（/api/... 或恰好等于 /api）→ 原样放行
  if (base && (path === base || path.startsWith(`${base}/`))) {
    return next(req);
  }

  return next(req.clone({ url: `${base}${path}` }));
};
