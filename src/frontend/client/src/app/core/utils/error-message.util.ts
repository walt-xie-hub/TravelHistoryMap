import { HttpErrorResponse } from '@angular/common/http';

/**
 * 从 HTTP / 网络错误中提取可展示的消息文案。
 * 与全局 error 拦截器使用同一套解析规则，供表单页内联展示错误。
 */
export function extractErrorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.error instanceof ErrorEvent) {
      return `网络异常：${error.error.message}`;
    }
    if (typeof error.error === 'string' && error.error.trim()) {
      return error.error;
    }
    if (error.error !== null && typeof error.error === 'object') {
      const body = error.error as { message?: string; title?: string };
      if (body.message) return String(body.message);
      if (body.title) return String(body.title);
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
  if (error instanceof Error) {
    return error.message;
  }
  return '操作失败，请稍后重试。';
}
