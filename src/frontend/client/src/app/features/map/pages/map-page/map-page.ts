import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { lastValueFrom } from 'rxjs';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { LoadingSpinner } from '@shared/components/loading-spinner/loading-spinner';
import { PageHeader } from '@shared/components/page-header/page-header';
import { UserService } from '@features/users/services/user.service';
import type { User } from '@features/users/models/user.model';
import type {
  AmapInfoWindow,
  AmapMap,
  AmapMarker,
  AmapNamespace,
} from '../../../../../types/amap';
import { MapSidePanel, MapUserOption } from '../../components/map-side-panel/map-side-panel';
import { AmapLoaderService } from '../../services/amap-loader.service';
import { TravelHistoryService } from '../../services/travel-history.service';
import type { TravelRangeRequest, TravelRecord } from '../../models/travel-record.model';
import { wgs84ToGcj02 } from '../../utils/coord';
import { TravelMarkerGroup, fmtDateTime, groupRecords } from '../../utils/travel-display';

const DEFAULT_CENTER = [104.1954, 35.8617] as const; // 中国全国视野
const DEFAULT_ZOOM = 5;
const FOCUS_ZOOM = 10;
const FIT_PADDING: number[] = [70, 70, 70, 70];
const USER_KEY = 'map.lastUserId';
const PAGE_SIZE = 100;

type MapState = 'idle' | 'ready' | 'missing-key' | 'error';

/**
 * 地图页：加载高德地图 JS API，展示所选用户的旅行足迹。
 * 决策来源：docs/adr/0003（AMap + WGS-84 存储 / GCJ-02 渲染）、docs/adr/0004（展示层规则）。
 */
@Component({
  selector: 'app-map-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyState, LoadingSpinner, MapSidePanel, PageHeader],
  templateUrl: './map-page.html',
  styleUrl: './map-page.scss',
})
export class MapPage implements AfterViewInit, OnDestroy {
  private readonly userService = inject(UserService);
  private readonly travelService = inject(TravelHistoryService);
  private readonly amapLoader = inject(AmapLoaderService);

  private readonly mapContainer =
    viewChild.required<ElementRef<HTMLDivElement>>('mapContainer');

  readonly users = signal<User[]>([]);
  readonly usersLoading = signal(true);
  readonly selectedUserId = signal<number | null>(null);
  readonly records = signal<TravelRecord[]>([]);
  readonly travelLoading = signal(false);
  readonly travelError = signal<string | null>(null);
  readonly mapState = signal<MapState>('idle');
  readonly mapErrorMsg = signal('');
  readonly selectedRecordId = signal<number | null>(null);

  readonly userOptions = computed<MapUserOption[]>(() =>
    this.users().map((u) => ({ id: u.id, name: u.name, email: u.email })),
  );

  /** 进行中/过去 的标注计数徽标（legend 用） */
  readonly hasRecords = computed(() => this.records().length > 0);

  private amap: AmapNamespace | null = null;
  private map: AmapMap | null = null;
  private infoWindow: AmapInfoWindow | null = null;
  private markers: AmapMarker[] = [];
  private markerPositionByGroupKey = new Map<string, [number, number]>();
  private groupByRecord = new Map<number, TravelMarkerGroup>();
  private range: TravelRangeRequest = { kind: 'all' };
  private loadedKey = '';
  private fetchSeq = 0;

  async ngAfterViewInit(): Promise<void> {
    this.initMap();
    await this.loadUsers();
  }

  ngOnDestroy(): void {
    this.map?.destroy();
    this.map = null;
    this.infoWindow = null;
    this.markers = [];
  }

  // ---------- 地图初始化 ----------

  private initMap(): void {
    if (!this.amapLoader.isConfigured) {
      this.mapState.set('missing-key');
      return;
    }
    this.amapLoader
      .load()
      .then(() => {
        if (!window.AMap) {
          this.failMap('高德地图 SDK 未就绪');
          return;
        }
        this.amap = window.AMap;
        this.createMap();
      })
      .catch((err: unknown) => {
        this.failMap(err instanceof Error ? err.message : '高德地图加载失败');
      });
  }

  private failMap(message: string): void {
    this.mapState.set('error');
    this.mapErrorMsg.set(message);
  }

  private createMap(): void {
    const amap = this.amap;
    if (!amap) return;
    this.map = new amap.Map(this.mapContainer().nativeElement, {
      viewMode: '2D',
      zoom: DEFAULT_ZOOM,
      center: [...DEFAULT_CENTER],
    });
    this.infoWindow = new amap.InfoWindow({
      offset: new amap.Pixel(0, -30),
      closeWhenClickMap: false,
    });
    this.mapState.set('ready');
    if (this.selectedUserId() === null || this.loadedKey !== this.currentKey()) {
      void this.refreshRecords();
    } else {
      this.renderMarkers();
      this.fitView();
    }
  }

  // ---------- 用户 ----------

  private async loadUsers(): Promise<void> {
    try {
      const all = await this.fetchAllUsers();
      this.users.set(all);
      const stored = Number(window.localStorage.getItem(USER_KEY));
      const preferred = Number.isFinite(stored) && stored > 0 ? stored : null;
      const chosen =
        preferred !== null && all.some((u) => u.id === preferred)
          ? preferred
          : (all[0]?.id ?? null);
      this.selectedUserId.set(chosen);
      if (chosen !== null) window.localStorage.setItem(USER_KEY, String(chosen));
      else window.localStorage.removeItem(USER_KEY);
    } finally {
      this.usersLoading.set(false);
    }
  }

  private async fetchAllUsers(): Promise<User[]> {
    const collected: User[] = [];
    let page = 1;
    // eslint-disable-next-line no-constant-condition
    while (true) {
      const result = await lastValueFrom(this.userService.getPaged(page, PAGE_SIZE));
      collected.push(...result.items);
      if (result.items.length < PAGE_SIZE) break;
      page += 1;
    }
    return collected;
  }

  onUserChange(userId: number): void {
    if (userId === this.selectedUserId()) return;
    this.selectedUserId.set(userId);
    window.localStorage.setItem(USER_KEY, String(userId));
    this.selectedRecordId.set(null);
    this.infoWindow?.close();
    void this.refreshRecords();
  }

  // ---------- 数据拉取（服务端时间窗过滤） ----------

  onFilterChange(request: TravelRangeRequest): void {
    this.range = request;
    this.selectedRecordId.set(null);
    this.infoWindow?.close();
    void this.refreshRecords();
  }

  private currentKey(): string {
    return `${this.selectedUserId()}|${this.range.kind}|${this.range.from ?? ''}|${this.range.to ?? ''}`;
  }

  private async refreshRecords(): Promise<void> {
    const userId = this.selectedUserId();
    if (userId === null || this.mapState() !== 'ready') return;

    const seq = ++this.fetchSeq;
    const key = this.currentKey();
    this.travelLoading.set(true);
    this.travelError.set(null);
    try {
      const rows = await this.travelService.getAllForUser(userId, {
        from: this.range.from,
        to: this.range.to,
      });
      if (seq !== this.fetchSeq) return;
      this.records.set(rows);
      this.loadedKey = key;
      this.renderMarkers();
      this.fitView();
    } catch {
      if (seq === this.fetchSeq) {
        this.records.set([]);
        this.travelError.set('加载旅行记录失败，请检查服务后重试。');
        this.renderMarkers();
        this.fitView();
      }
    } finally {
      if (seq === this.fetchSeq) this.travelLoading.set(false);
    }
  }

  // ---------- 标注渲染（同坐标合并，见 ADR-0004） ----------

  private renderMarkers(): void {
    const map = this.map;
    const amap = this.amap;
    if (!map || !amap) return;

    for (const marker of this.markers) map.remove(marker);
    this.markers = [];
    this.markerPositionByGroupKey.clear();
    this.groupByRecord.clear();
    this.infoWindow?.close();

    for (const group of groupRecords(this.records())) {
      for (const record of group.records) this.groupByRecord.set(record.id, group);

      const [gcjLng, gcjLat] = wgs84ToGcj02(group.lng, group.lat);
      const position: [number, number] = [gcjLng, gcjLat];
      const count = group.records.length;
      const ongoing = group.records.some((r) => r.departedAt === null);

      const content = document.createElement('div');
      content.className = [
        'tm-marker',
        count > 1 ? 'tm-marker--merged' : '',
        ongoing ? 'tm-marker--ongoing' : '',
      ]
        .filter(Boolean)
        .join(' ');
      if (count > 1) content.textContent = String(count);

      const marker = new amap.Marker({
        position,
        content,
        offset: new amap.Pixel(0, 0),
        zIndex: ongoing ? 1000 : 500,
        title: group.records[0]!.locationName,
      });
      marker.on('click', () => this.openInfo(group, position));
      map.add(marker);

      this.markers.push(marker);
      this.markerPositionByGroupKey.set(group.key, position);
    }
  }

  private fitView(): void {
    const map = this.map;
    if (!map) return;
    if (this.markers.length === 0) {
      map.setZoomAndCenter(DEFAULT_ZOOM, [...DEFAULT_CENTER]);
      return;
    }
    map.setFitView(this.markers, false, FIT_PADDING);
  }

  // ---------- 气泡与联动 ----------

  private openInfo(group: TravelMarkerGroup, position: readonly [number, number]): void {
    if (!this.infoWindow) return;
    this.infoWindow.setContent(this.buildInfoContent(group));
    this.infoWindow.open(this.map!, [...position]);
  }

  private buildInfoContent(group: TravelMarkerGroup): HTMLElement {
    const root = document.createElement('div');
    root.className = 'tm-info';

    const head = document.createElement('div');
    head.className = 'tm-info__title';
    head.textContent = group.records[0]!.locationName;
    root.appendChild(head);

    const sub = document.createElement('p');
    sub.className = 'tm-info__sub';
    sub.textContent =
      group.records.length > 1
        ? `${group.records.length} 次停留 · ${group.lat.toFixed(5)}, ${group.lng.toFixed(5)}（WGS-84）`
        : `坐标 ${group.lat.toFixed(5)}, ${group.lng.toFixed(5)}（WGS-84）`;
    root.appendChild(sub);

    const list = document.createElement('div');
    list.className = 'tm-info__list';
    for (const record of group.records) {
      const row = document.createElement('button');
      row.type = 'button';
      row.className = 'tm-info__row';

      const line = document.createElement('span');
      line.className = 'tm-info__row-line';
      const dates = document.createElement('span');
      dates.textContent = record.departedAt
        ? `${fmtDateTime(record.arrivedAt)} → ${fmtDateTime(record.departedAt)}`
        : `${fmtDateTime(record.arrivedAt)} · 至今`;
      line.appendChild(dates);
      if (!record.departedAt) {
        const badge = document.createElement('span');
        badge.className = 'tm-info__badge';
        badge.textContent = '进行中';
        line.appendChild(badge);
      }
      row.appendChild(line);
      row.addEventListener('click', () => {
        this.selectedRecordId.set(record.id);
        this.infoWindow?.close();
      });
      list.appendChild(row);
    }
    root.appendChild(list);
    return root;
  }

  onRecordSelect(recordId: number): void {
    this.selectedRecordId.set(recordId);
    const group = this.groupByRecord.get(recordId);
    const position = group ? this.markerPositionByGroupKey.get(group.key) : undefined;
    const map = this.map;
    if (!map || !position) return;
    map.setCenter([...position]);
    if (map.getZoom() < FOCUS_ZOOM) map.setZoom(FOCUS_ZOOM);
    this.openInfo(group!, position);
  }

  onFitAll(): void {
    this.fitView();
    this.infoWindow?.close();
  }

  onRetry(): void {
    void this.refreshRecords();
  }
}
