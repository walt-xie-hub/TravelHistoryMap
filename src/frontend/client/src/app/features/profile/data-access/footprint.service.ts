import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { lastValueFrom } from 'rxjs';
import { FootprintOverview } from '@core/models/footprint.model';

/**
 * 足迹概览 API 客户端（ADR-0025）。
 *
 * 注意它打到的是 **user-service**（相对路径 → `apiBaseUrl`），而不是 travel-service。
 * 这正是本功能要验证的那条链路：浏览器只跟自己的服务说话，
 * 跨服务那一跳由 user-service 以**登录用户的名义**（用户委托令牌）完成 ——
 * 用户令牌不会流向 travel-service，它只到 identity-service 为止。
 *
 * 下游不可用时后端回 503（而不是降级成"空概览"），因此调用方必须处理失败。
 */
@Injectable({ providedIn: 'root' })
export class FootprintService {
  private readonly http = inject(HttpClient);

  get(): Promise<FootprintOverview> {
    return lastValueFrom(this.http.get<FootprintOverview>('users/me/footprint'));
  }
}
