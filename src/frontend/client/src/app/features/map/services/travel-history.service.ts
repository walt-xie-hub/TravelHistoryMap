/**
 * travel-history API 客户端。travel-history 是独立微服务（dev 由 compose 暴露在
 * http://localhost:8081/api，见 docker-compose.dev.yml 的 travel-server），
 * 因此请求拼到 environment.travelApiBaseUrl 的绝对地址，不经 user-service 的 apiBaseUrl。
 */
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { lastValueFrom } from 'rxjs';
import { environment } from '@env/environment';
import {
  TravelPagedResult,
  TravelRecord,
} from '../models/travel-record.model';

@Injectable({ providedIn: 'root' })
export class TravelHistoryService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.travelApiBaseUrl.replace(/\/+$/, '');

  /** 单页查询（后端上限 pageSize=100，越界会被钳制为 10） */
  getPaged(
    userId: number,
    page: number,
    pageSize: number,
    from?: string,
    to?: string,
  ): Promise<TravelPagedResult<TravelRecord>> {
    let params = new HttpParams()
      .set('userId', String(userId))
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return lastValueFrom(this.http.get<TravelPagedResult<TravelRecord>>(`${this.base}/travels`, { params }));
  }

  /** 按用户 + 可选到达时间窗（UTC ISO）全量拉取（自动翻页），按到达时间倒序。 */
  async getAllForUser(
    userId: number,
    opts: { from?: string; to?: string } = {},
  ): Promise<TravelRecord[]> {
    const pageSize = 100;
    const collected: TravelRecord[] = [];
    let page = 1;
    // eslint-disable-next-line no-constant-condition
    while (true) {
      const result = await this.getPaged(userId, page, pageSize, opts.from, opts.to);
      collected.push(...result.items);
      if (collected.length >= result.totalCount || page >= result.totalPages) break;
      page += 1;
    }
    return collected;
  }
}
