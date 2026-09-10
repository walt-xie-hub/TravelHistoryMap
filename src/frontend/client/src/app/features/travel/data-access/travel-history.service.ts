/**
 * travel-history API 客户端。travel-history 是独立微服务（dev 由 compose 暴露在
 * http://localhost:8081/api，见 docker-compose.dev.yml 的 travel-server），
 * 因此请求拼到 environment.travelApiBaseUrl 的绝对地址，不经 user-service 的 apiBaseUrl。
 * ADR-0005 起归属 = 当前登录用户：不传 userId，由 auth 拦截器附加 Bearer token。
 */
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { lastValueFrom } from 'rxjs';
import { environment } from '@env/environment';
import {
  CreateTravelRequest,
  PublicShareSnapshot,
  ShareCreated,
  TravelListResult,
  TravelPagedResult,
  TravelImage,
  TravelRecord,
} from '../models/travel-record.model';

@Injectable({ providedIn: 'root' })
export class TravelHistoryService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.travelApiBaseUrl.replace(/\/+$/, '');

  /** 单页查询（后端上限 pageSize=100，越界会被钳制为 10）；favoriteOnly 只看精选收藏（ADR-0014） */
  getPaged(
    page: number,
    pageSize: number,
    from?: string,
    to?: string,
    favoriteOnly = false,
  ): Promise<TravelPagedResult<TravelRecord>> {
    let params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    if (favoriteOnly) params = params.set('favoriteOnly', 'true');
    return lastValueFrom(this.http.get<TravelPagedResult<TravelRecord>>(`${this.base}/travels`, { params }));
  }

  /** 全量拉取当前登录用户的停留记录（可选到达时间窗，UTC ISO），自动翻页，按到达时间倒序。 */
  async getAll(opts: { from?: string; to?: string } = {}): Promise<TravelRecord[]> {
    const { items } = await this.collectPages((page) => this.getPaged(page, 100, opts.from, opts.to));
    return items;
  }

  /**
   * 全量拉取当前登录用户的停留记录，带**上限保护**（ADR-0004 的「全部足迹」用）：
   * 最多取 limit 条（100/页自动翻页），并按服务端总数告知是否被截断。
   */
  async getAllUpTo(limit: number): Promise<TravelListResult> {
    const { items, totalCount } = await this.collectPages((page) =>
      this.getPaged(page, Math.min(100, Math.max(1, limit))),
    );
    return { items: items.slice(0, limit), truncated: totalCount > items.length };
  }

  /** 逐页收集（最多到 limit 条）：全量拉取与回收站共用，避免三份相同的翻页循环 */
  private async collectPages(
    fetchPage: (page: number) => Promise<TravelPagedResult<TravelRecord>>,
    limit = Number.POSITIVE_INFINITY,
  ): Promise<{ items: TravelRecord[]; totalCount: number }> {
    const items: TravelRecord[] = [];
    let totalCount = 0;
    let page = 1;
    while (items.length < limit) {
      const result = await fetchPage(page);
      totalCount = result.totalCount;
      items.push(...result.items);
      if (result.items.length === 0 || page >= result.totalPages) break;
      page += 1;
    }
    return { items, totalCount };
  }

  create(request: CreateTravelRequest): Promise<TravelRecord> {
    return lastValueFrom(this.http.post<TravelRecord>(`${this.base}/travels`, request));
  }

  /** 全量更新旅行记录（PUT 全量替换：地点快照/时间/描述一并回传；本页仅编辑描述，其余沿用已加载值）。 */
  update(id: number, request: CreateTravelRequest): Promise<TravelRecord> {
    return lastValueFrom(this.http.put<TravelRecord>(`${this.base}/travels/${id}`, request));
  }

  /** 全量删除 userId 名下的记录——移入回收站（软删除，ADR-0013），可恢复。 */
  delete(id: number): Promise<void> {
    return lastValueFrom(this.http.delete<void>(`${this.base}/travels/${id}`));
  }

  /** 回收站分页：已软删除记录（最近删除在前）。 */
  getTrashPaged(page: number, pageSize: number): Promise<TravelPagedResult<TravelRecord>> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return lastValueFrom(this.http.get<TravelPagedResult<TravelRecord>>(`${this.base}/travels/trash`, { params }));
  }

  /** 回收站全量拉取（自动翻页）。 */
  async getAllTrash(): Promise<TravelRecord[]> {
    const { items } = await this.collectPages((page) => this.getTrashPaged(page, 100));
    return items;
  }

  /** 从回收站恢复记录。 */
  restore(id: number): Promise<void> {
    return lastValueFrom(this.http.post<void>(`${this.base}/travels/${id}/restore`, null));
  }

  /** 彻底删除记录（含图片与媒体，不可恢复）。 */
  deletePermanently(id: number): Promise<void> {
    return lastValueFrom(this.http.delete<void>(`${this.base}/travels/${id}/permanent`));
  }

  getById(id: number): Promise<TravelRecord> {
    return lastValueFrom(this.http.get<TravelRecord>(`${this.base}/travels/${id}`));
  }

  getImages(id: number): Promise<TravelImage[]> {
    return lastValueFrom(this.http.get<TravelImage[]>(`${this.base}/travels/${id}/images`));
  }

  async uploadImage(id: number, file: File): Promise<TravelImage> {
    const body = new FormData();
    body.append('file', file, file.name);
    return lastValueFrom(this.http.post<TravelImage>(`${this.base}/travels/${id}/images`, body));
  }

  /** 删除某条记录下的单张图片（后端清理媒体文件并删除数据库行）。 */
  deleteImage(id: number, imageId: number): Promise<void> {
    return lastValueFrom(this.http.delete<void>(`${this.base}/travels/${id}/images/${imageId}`));
  }

  /** 创建只读分享快照（ADR-0012），返回 token 与相对路径 /s/{token}。 */
  shareTravels(travelIds: number[]): Promise<ShareCreated> {
    return lastValueFrom(this.http.post<ShareCreated>(`${this.base}/travels/share`, { travelIds }));
  }

  /** 公开读取分享快照（无需登录；凭不可猜测 token）。 */
  getShareSnapshot(token: string): Promise<PublicShareSnapshot> {
    return lastValueFrom(this.http.get<PublicShareSnapshot>(`${this.base}/share-snapshots/${encodeURIComponent(token)}`));
  }

  resolveMediaUrl(path: string): string {
    return path.startsWith('http') ? path : `${this.base.replace(/\/api$/, '')}${path}`;
  }

  getMediaBlob(path: string): Promise<Blob> {
    return lastValueFrom(this.http.get(this.resolveMediaUrl(path), { responseType: 'blob' }));
  }
}
