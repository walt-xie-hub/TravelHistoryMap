/**
 * 媒体 URL 句柄（架构评审候选 #1 的落地）。
 *
 * 背景：媒体端点带登录态（`/api/travels/{id}/images/.../{variant}` 需 Bearer），
 * `<img src>` 无法直连，各页面都得“鉴权 fetch blob → URL.createObjectURL → 销毁时
 * URL.revokeObjectURL”。此前该套记账在 travel-detail / photo-wall 各手抄一份。
 *
 * 本模块把它收敛成一个深模块：小接口（urlFor / dispose），大实现（鉴权拉取、
 * 按路径缓存、in-flight 去重、统一回收）。宿主只依赖注入的 fetchBlob，便于测试换 fake。
 */

export interface MediaUrlDeps {
  /** 取回媒体 blob（通常带鉴权）。由调用方提供，测试注入 fake。 */
  fetchBlob: (path: string) => Promise<Blob>;
}

export interface MediaUrlHandle {
  /**
   * 给定媒体路径，返回可直接用于 `<img>` 的对象 URL。
   * 同路径在句柄生命周期内缓存并复用（并发调用共享同一次拉取）。
   */
  urlFor(path: string): Promise<string>;
  /** 一次性回收本句柄创建的全部对象 URL 并清空缓存（宿主销毁时调用）。 */
  dispose(): void;
}

export function createMediaUrlHandle(deps: MediaUrlDeps): MediaUrlHandle {
  // path -> 正在解析 / 已解析的对象 URL。存 Promise 以让并发调用共享同一次 fetch。
  const cache = new Map<string, Promise<string>>();

  async function fetchUrl(path: string): Promise<string> {
    const blob = await deps.fetchBlob(path);
    return URL.createObjectURL(blob);
  }

  return {
    urlFor(path: string): Promise<string> {
      const existing = cache.get(path);
      if (existing) return existing;
      const pending = fetchUrl(path).catch((error) => {
        // 失败不让坏条目滞留，下次调用可重试
        cache.delete(path);
        throw error;
      });
      cache.set(path, pending);
      return pending;
    },

    dispose(): void {
      for (const pending of cache.values()) {
        // 已解析的 URL 立即回收；仍在拉取的等 resolve 后回收
        pending
          .then((url) => URL.revokeObjectURL(url))
          .catch(() => undefined);
      }
      cache.clear();
    },
  };
}
