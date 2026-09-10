/**
 * 坐标 → 城市短名的**派生**查询（ADR-0017）。
 *
 * 用途：地图上的历史记录可能没有 City 快照（写入时逆地理失败或数据早于 ADR-0015），
 * 但它们在语义上仍属于某个城市（如「黄圃镇人民政府」→ 中山）。这里在**读取期**按坐标派生城市，
 * 只用于地图分组，**不写回数据库**（快照语义见 ADR-0015）。
 *
 * 约定：
 * - 按 WGS-84 4 位小数（≈11 米）做缓存键 + 并发去重，同一坐标只问一次高德；
 * - 失败/超时/无结果一律 resolve(null)，调用方回退到按坐标合并；
 * - 派生查询不阻塞地图首帧：调用方先按快照渲染，结果回来后再重渲染标记。
 */
import { Injectable, inject } from '@angular/core';
import { AmapLoaderService } from './amap-loader.service';
import { cityFromComponents } from '../utils/amap-city.util';

/** 缓存键（本服务内部用：4 位小数 ≈11 米，与地点标记的分组容差无关，各自独立） */
function coordKey(lng: number, lat: number): string {
  return `${lng.toFixed(4)},${lat.toFixed(4)}`;
}

/** 一次逆地理的最长等待（毫秒）：超时按“查不到”处理，避免标记永远等在那里 */
const GEOCODE_TIMEOUT_MS = 6000;

@Injectable({ providedIn: 'root' })
export class AmapReverseGeocodeService {
  private readonly loader = inject(AmapLoaderService);
  /** 只缓存**成功**结果：失败/超时下次还有机会重试（避免一次限流把地点永久退化成地点标记） */
  private readonly cache = new Map<string, Promise<string>>();

  /** 批量派生：按坐标去重后逐个取缓存或发起请求，返回与入参**同序**的结果（查不到为 null） */
  async resolveCities(coords: readonly { lng: number; lat: number }[]): Promise<(string | null)[]> {
    return Promise.all(coords.map((coord) => this.resolveCity(coord.lng, coord.lat)));
  }

  private resolveCity(lng: number, lat: number): Promise<string | null> {
    const key = coordKey(lng, lat);
    const cached = this.cache.get(key);
    if (cached) return cached;

    const pending = this.request(lng, lat).then(
      (city) => {
        if (!city) this.cache.delete(key);
        return city;
      },
      () => {
        this.cache.delete(key);
        return null;
      },
    );
    // 并发去重：同一个坐标只问一次（先占位，失败再移除）
    this.cache.set(key, pending as Promise<string>);
    return pending;
  }

  private async request(lng: number, lat: number): Promise<string | null> {
    await this.loader.load();
    const amap = window.AMap;
    if (!amap?.plugin) return null;

    return new Promise<string | null>((resolve) => {
      let settled = false;
      const finish = (city: string | null): void => {
        if (settled) return;
        settled = true;
        window.clearTimeout(timer);
        resolve(city);
      };
      const timer = window.setTimeout(() => finish(null), GEOCODE_TIMEOUT_MS);

      try {
        amap.plugin!(['AMap.Geocoder'], () => {
          if (!amap.Geocoder) {
            finish(null);
            return;
          }
          const geocoder = new amap.Geocoder({ radius: 1000 });
          geocoder.getAddress([lng, lat], (status, result) => {
            finish(status === 'complete' ? cityFromComponents(result.regeocode?.addressComponent) || null : null);
          });
        });
      } catch {
        finish(null);
      }
    });
  }
}
