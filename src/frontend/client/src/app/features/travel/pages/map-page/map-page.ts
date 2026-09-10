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
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { Router } from '@angular/router';
import { LoadingSpinner } from '@shared/components/loading-spinner/loading-spinner';
import { PageHeader } from '@shared/components/page-header/page-header';
import type {
  AmapInfoWindow,
  AmapMap,
  AmapMarker,
  AmapNamespace,
  AmapLngLat,
  AmapPlaceResult,
} from '../../../../../types/amap';
import { MapSidePanel } from '../../components/map-side-panel/map-side-panel';
import { TravelShareDialog } from '../../components/travel-share-dialog/travel-share-dialog';
import { MapTimeline } from '../../components/map-timeline/map-timeline';
import type { TimelineRun } from '../../utils/timeline.util';
import { AmapLoaderService } from '../../data-access/amap-loader.service';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelRangeRequest, TravelRecord } from '../../models/travel-record.model';
import { wgs84ToGcj02 } from '../../utils/coord';
import { TravelMarkerGroup, fmtDateTime, groupRecords } from '../../utils/travel-display';

const DEFAULT_CENTER = [104.1954, 35.8617] as const; // 中国全国视野
const DEFAULT_ZOOM = 5;
const FOCUS_ZOOM = 10;
/** 时光轴点击节点定位的更高保底 zoom：定位到“具体地点”而非停留在城市视野（ADR-0015）。 */
const TIMELINE_FOCUS_ZOOM = 12;
const FIT_PADDING: number[] = [70, 70, 70, 70];
/** 停留记录分页大小（每页 10 条）；地图标注与时光轴跟随“当前页”数据（ADR-0004）。 */
const PAGE_SIZE = 10;

type MapState = 'idle' | 'ready' | 'missing-key' | 'error';

/**
 * 地图页：加载高德地图 JS API，展示【当前登录用户】的旅行足迹。
 * ADR-0005：不再有“用户下拉”，足迹归属来自登录态（token），服务端只返回本人记录。
 * 决策来源：docs/adr/0003（AMap + WGS-84 存储 / GCJ-02 渲染）、0004（展示层规则）、0005（登录化）。
 */
@Component({
  selector: 'app-map-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyState, LoadingSpinner, MapSidePanel, PageHeader, TravelShareDialog, MapTimeline],
  templateUrl: './map-page.html',
  styleUrl: './map-page.scss',
})
export class MapPage implements AfterViewInit, OnDestroy {
  private readonly travelService = inject(TravelHistoryService);
  private readonly router = inject(Router);
  private readonly amapLoader = inject(AmapLoaderService);

  private readonly mapContainer =
    viewChild.required<ElementRef<HTMLDivElement>>('mapContainer');

  readonly records = signal<TravelRecord[]>([]);
  readonly shareOpen = signal(false);
  readonly travelLoading = signal(false);
  readonly travelError = signal<string | null>(null);
  readonly mapState = signal<MapState>('idle');
  readonly mapErrorMsg = signal('');
  readonly selectedRecordId = signal<number | null>(null);
  readonly page = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly searchKeyword = signal('');
  readonly searchResults = signal<AmapPlaceResult[]>([]);
  readonly searchLoading = signal(false);
  readonly searchError = signal('');

  /** 进行中/过去 的标注计数徽标（legend 用） */
  readonly hasRecords = computed(() => this.records().length > 0);

  private amap: AmapNamespace | null = null;
  private map: AmapMap | null = null;
  private infoWindow: AmapInfoWindow | null = null;
  private markers: AmapMarker[] = [];
  private searchMarker: AmapMarker | null = null;
  private markerPositionByGroupKey = new Map<string, [number, number]>();
  private groupByRecord = new Map<number, TravelMarkerGroup>();
  private range: TravelRangeRequest = { kind: 'all' };
  private loadedKey = '';
  private fetchSeq = 0;

  ngAfterViewInit(): void {
    this.initMap();
  }

  ngOnDestroy(): void {
    this.map?.destroy();
    this.map = null;
    this.infoWindow = null;
    this.markers = [];
    this.searchMarker = null;
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
    if (this.loadedKey !== this.currentKey()) void this.refreshRecords();
    else {
      this.renderMarkers();
      this.fitView();
    }
  }

  // ---------- 数据拉取（归属=登录用户，服务端时间窗过滤） ----------

  onFilterChange(request: TravelRangeRequest): void {
    this.range = request;
    this.page.set(1);
    this.selectedRecordId.set(null);
    this.infoWindow?.close();
    void this.refreshRecords();
  }

  private currentKey(): string {
    return `${this.range.kind}|${this.range.from ?? ''}|${this.range.to ?? ''}|${this.page()}`;
  }

  private async refreshRecords(): Promise<void> {
    if (this.mapState() !== 'ready') return;

    const seq = ++this.fetchSeq;
    const key = this.currentKey();
    this.travelLoading.set(true);
    this.travelError.set(null);
    try {
      const result = await this.travelService.getPaged(
        this.page(),
        PAGE_SIZE,
        this.range.from,
        this.range.to,
      );
      if (seq !== this.fetchSeq) return;
      this.records.set(result.items);
      this.totalPages.set(result.totalPages);
      this.totalCount.set(result.totalCount);
      this.loadedKey = key;
      this.renderMarkers();
      this.fitView();
    } catch {
      if (seq === this.fetchSeq) {
        this.records.set([]);
        this.totalPages.set(1);
        this.totalCount.set(0);
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
      const markerKind = count > 1 ? 'merged' : ongoing ? 'ongoing' : 'ordinary';

      const content = document.createElement('div');
      content.className = [
        'tm-marker',
        `tm-marker--${markerKind}`,
      ]
        .filter(Boolean)
        .join(' ');
      content.setAttribute(
        'aria-label',
        count > 1 ? `多次到访，共 ${count} 次` : ongoing ? '进行中' : '普通停留',
      );

      const earLeft = document.createElement('span');
      earLeft.className = 'tm-marker__ear tm-marker__ear--left';
      const earRight = document.createElement('span');
      earRight.className = 'tm-marker__ear tm-marker__ear--right';
      const face = document.createElement('span');
      face.className = 'tm-marker__face';
      const eyeLeft = document.createElement('span');
      eyeLeft.className = 'tm-marker__eye tm-marker__eye--left';
      const eyeRight = document.createElement('span');
      eyeRight.className = 'tm-marker__eye tm-marker__eye--right';
      const muzzle = document.createElement('span');
      muzzle.className = 'tm-marker__muzzle';
      face.append(eyeLeft, eyeRight, muzzle);
      content.append(earLeft, earRight, face);

      if (count > 1) {
        const countBadge = document.createElement('span');
        countBadge.className = 'tm-marker__count';
        countBadge.textContent = String(count);
        content.appendChild(countBadge);
      }

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

  async searchPlace(): Promise<void> {
    const keyword = this.searchKeyword().trim();
    if (!keyword) return;
    const amap = this.amap;
    if (!amap || !this.map) {
      this.searchError.set('地图正在加载，请稍后再搜索。');
      return;
    }
    if (!amap.plugin) {
      this.searchError.set('地点搜索服务不可用，请检查高德地图配置。');
      return;
    }
    this.searchLoading.set(true);
    this.searchError.set('');
    this.searchResults.set([]);
    try {
      await new Promise<void>((resolve) => {
        amap.plugin!(['AMap.PlaceSearch'], () => {
          if (!amap.PlaceSearch) {
            this.searchError.set('地点搜索插件加载失败，请刷新页面后重试。');
            resolve();
            return;
          }
          const searcher = new amap.PlaceSearch!({ pageSize: 8 });
          searcher.search(keyword, (status, result) => {
            const places = status === 'complete' ? result.poiList?.pois ?? [] : [];
            this.searchResults.set(places);
            if (!places.length) this.searchError.set('没有找到匹配的位置。');
            resolve();
          });
        });
      });
    } finally {
      this.searchLoading.set(false);
    }
  }

  selectSearchPlace(place: AmapPlaceResult): void {
    const position = this.placeCoordinates(place.location);
    if (!position || !this.map || !this.amap) return;
    this.searchResults.set([]);
    this.searchKeyword.set(place.name ?? this.searchKeyword());
    if (this.searchMarker) this.map.remove(this.searchMarker);
    this.searchMarker = new this.amap.Marker({
      position,
      title: place.name ?? '搜索位置',
      zIndex: 1200,
    });
    this.map.add(this.searchMarker);
    this.map.setZoomAndCenter(15, position);

    const content = document.createElement('div');
    content.className = 'tm-search-info';
    const title = document.createElement('strong');
    title.textContent = place.name ?? '搜索位置';
    content.appendChild(title);
    const address = document.createElement('span');
    address.textContent = place.address ?? '地址信息暂无';
    content.appendChild(address);
    const coordinates = document.createElement('span');
    coordinates.textContent = `坐标：${position[1].toFixed(6)}, ${position[0].toFixed(6)}`;
    content.appendChild(coordinates);
    this.infoWindow?.setContent(content);
    this.infoWindow?.open(this.map, position);
  }

  private placeCoordinates(location: AmapPlaceResult['location']): AmapLngLat | null {
    if (!location) return null;
    return Array.isArray(location) ? location : [location.getLng(), location.getLat()];
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
    const detail = document.createElement('button');
    detail.type = 'button';
    detail.className = 'tm-info__detail';
    detail.textContent = '查看旅游详情';
    detail.addEventListener('click', () => {
      this.infoWindow?.close();
      void this.router.navigate(['/travels', group.records[0]!.id]);
    });
    root.appendChild(detail);

    // “再来一次”：预填该点（名称/坐标/城市）跳新建页
    const first = group.records[0]!;
    const again = document.createElement('button');
    again.type = 'button';
    again.className = 'tm-info__detail';
    again.textContent = '＋ 再来一次到访';
    again.addEventListener('click', () => {
      this.infoWindow?.close();
      void this.router.navigate(['/travels/new'], {
        state: {
          prefill: {
            locationName: first.locationName,
            longitude: first.longitude,
            latitude: first.latitude,
            city: first.city ?? null,
          },
        },
      });
    });
    root.appendChild(again);
    return root;
  }

  /** 侧栏/搜索选中记录：居中到其 marker 并弹信息窗（保底 FOCUS_ZOOM）。 */
  onRecordSelect(recordId: number): void {
    this.focusOnMap(recordId, FOCUS_ZOOM);
  }

  /** 时光轴点击节点（focus 输出）：定位 run 最新一条所在 marker，用更高保底 zoom（ADR-0015）。 */
  onTimelineFocus(recordId: number): void {
    this.focusOnMap(recordId, TIMELINE_FOCUS_ZOOM);
  }

  private focusOnMap(recordId: number, minZoom: number): void {
    this.selectedRecordId.set(recordId);
    const group = this.groupByRecord.get(recordId);
    const position = group ? this.markerPositionByGroupKey.get(group.key) : undefined;
    const map = this.map;
    if (!map || !position) return;
    map.setCenter([...position]);
    if (map.getZoom() < minZoom) map.setZoom(minZoom);
    this.openInfo(group!, position);
  }

  async onRecordDelete(recordId: number): Promise<void> {
    const record = this.records().find((item) => item.id === recordId);
    if (!record) return;
    // ADR-0013：删除 = 移入回收站，可随时恢复；彻底删除请在回收站操作
    const confirmed = window.confirm(
      `确定要将“${record.locationName}”这条停留记录移入回收站吗？可随时从回收站恢复。`,
    );
    if (!confirmed) return;

    this.travelLoading.set(true);
    this.travelError.set(null);
    try {
      await this.travelService.delete(recordId);
      if (this.selectedRecordId() === recordId) this.selectedRecordId.set(null);
      this.infoWindow?.close();
      if (this.records().length === 1 && this.page() > 1) {
        this.page.update(value => value - 1);
      }
      await this.refreshRecords();
    } catch {
      this.travelError.set('移入回收站失败，请稍后重试。');
    } finally {
      this.travelLoading.set(false);
    }
  }

  /** 时光轴删除：确认后把整个 City run 逐条移入回收站并刷新（ADR-0015）。 */
  async onDeleteRun(run: TimelineRun): Promise<void> {
    const ids = run.records.map((item) => item.id);
    const label = run.records.length > 1 ? `${run.records.length} 条停留` : `“${run.records[0]?.locationName}”`;
    if (!window.confirm(`确定要将该停留${run.records.length > 1 ? '（' + run.records[0]?.locationName + ' 等 ' + run.records.length + ' 条）' : label}移入回收站吗？可随时从回收站恢复。`)) return;

    this.travelLoading.set(true);
    this.travelError.set(null);
    try {
      for (const id of ids) {
        await this.travelService.delete(id);
      }
      if (ids.includes(this.selectedRecordId() ?? -1)) this.selectedRecordId.set(null);
      this.infoWindow?.close();
      if (this.records().length === ids.length && this.page() > 1) {
        this.page.update((value) => value - 1);
      }
      await this.refreshRecords();
    } catch {
      this.travelError.set('移入回收站失败，请稍后重试。');
    } finally {
      this.travelLoading.set(false);
    }
  }

  onRecordDetail(recordId: number): void {
    void this.router.navigate(['/travels', recordId]);
  }

  onPageChange(nextPage: number): void {
    if (nextPage < 1 || nextPage > this.totalPages() || nextPage === this.page()) return;
    this.page.set(nextPage);
    this.selectedRecordId.set(null);
    this.infoWindow?.close();
    void this.refreshRecords();
  }

  onFitAll(): void {
    this.fitView();
    this.infoWindow?.close();
  }

  onRetry(): void {
    void this.refreshRecords();
  }
}
