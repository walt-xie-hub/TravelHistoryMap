/**
 * 高德地图 JS API 2.0 动态加载器。
 * - 零 npm 依赖：首次使用时注入官方 script，之后复用同一 Promise。
 * - 未配置 key / securityJsCode 时抛出 AmapNotConfiguredError，由页面给出可操作提示
 *   （申请步骤见 README「地图密钥」；dev 填入 src/environments/environment.ts 的 amap 字段）。
 * - 安全密钥须在引入 SDK 前通过 window._AMapSecurityConfig 注入（v2.0 约定）。
 */
import { Injectable } from '@angular/core';
import { environment } from '@env/environment';

export class AmapNotConfiguredError extends Error {
  constructor() {
    super('高德地图 key / securityJsCode 未配置');
    this.name = 'AmapNotConfiguredError';
  }
}

const SCRIPT_ID = 'amap-js-script';
const CALLBACK_NAME = '__travelMapAmapLoaded';
const LOAD_TIMEOUT_MS = 20_000;

@Injectable({ providedIn: 'root' })
export class AmapLoaderService {
  /** 是否已配置高德密钥（缺任一项都视为未配置） */
  readonly isConfigured: boolean = Boolean(
    environment.amap.key?.trim() && environment.amap.securityJsCode?.trim(),
  );

  private loading: Promise<void> | null = null;

  /** 确保 SDK 可用；已配置且加载成功则 resolve，否则 reject。 */
  load(): Promise<void> {
    if (!this.isConfigured) return Promise.reject(new AmapNotConfiguredError());
    if (typeof window === 'undefined' || window.AMap) return Promise.resolve();
    if (this.loading) return this.loading;

    this.loading = new Promise<void>((resolve, reject) => {
      const finish = (err?: Error): void => {
        window.clearTimeout(timer);
        if (err) reject(err);
        else resolve();
      };

      // 高德官方 script：加载完成会调用 window[CALLBACK_NAME]；部分失败场景（key 非法/白名单不符）
      // 不会触发回调，因此用 onload + 超时兜底，给出明确错误而不是永久 pending。
      const timer = window.setTimeout(() => finish(new Error('高德地图 SDK 加载超时')), LOAD_TIMEOUT_MS);

      const existing = document.getElementById(SCRIPT_ID);
      if (existing) {
        if (window.AMap) {
          finish();
          return;
        }
        existing.addEventListener('load', () =>
          finish(window.AMap ? undefined : new Error('高德地图 SDK 加载异常')),
          { once: true },
        );
        existing.addEventListener('error', () => finish(new Error('高德地图 SDK 加载失败')), {
          once: true,
        });
        return;
      }

      window._AMapSecurityConfig = { securityJsCode: environment.amap.securityJsCode! };
      (window as unknown as Record<string, unknown>)[CALLBACK_NAME] = () => finish();
      window.setTimeout(() => {
        if (window.AMap) finish();
      }, 0);

      const script = document.createElement('script');
      script.id = SCRIPT_ID;
      script.async = true;
      script.src = `https://webapi.amap.com/maps?v=2.0&key=${encodeURIComponent(
        environment.amap.key!,
      )}&callback=${CALLBACK_NAME}`;
      script.onload = () => {
        if (window.AMap) finish();
      };
      script.onerror = () => finish(new Error('高德地图 SDK 网络加载失败'));
      document.head.appendChild(script);
    });

    // 失败允许下次重试
    this.loading.catch(() => {
      this.loading = null;
    });
    return this.loading;
  }
}
