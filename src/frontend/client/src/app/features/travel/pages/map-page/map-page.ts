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
} from '../../../../../types/amap';
import { MapSidePanel } from '../../components/map-side-panel/map-side-panel';
import { TravelShareDialog } from '../../components/travel-share-dialog/travel-share-dialog';
import { MapTimeline } from '../../components/map-timeline/map-timeline';
import type { TimelineRun } from '../../utils/timeline.util';
import { AmapLoaderService } from '../../data-access/amap-loader.service';
import { AmapReverseGeocodeService } from '../../data-access/amap-reverse-geocode.service';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelRangeRequest, TravelRecord } from '../../models/travel-record.model';
import { wgs84ToGcj02 } from '../../utils/coord';
import { MapMarkerGroup, effectiveCity, fmtDateTime, groupMapMarkers, iconSourceOf } from '../../utils/travel-display';
import { iconForRecord, sharedTravelIcon, travelIconMarkup } from '../../utils/travel-icon.util';

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
  private readonly reverseGeocode = inject(AmapReverseGeocodeService);

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
  /** 无 City 快照记录的**派生城市**（recordId → 城市短名）：只用于地图分组，ADR-0017 */
  readonly derivedCities = signal<ReadonlyMap<number, string>>(new Map());
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);

  /** 进行中/过去 的标注计数徽标（legend 用） */
  readonly hasRecords = computed(() => this.records().length > 0);

  private amap: AmapNamespace | null = null;
  private map: AmapMap | null = null;
  private infoWindow: AmapInfoWindow | null = null;
  private markers: AmapMarker[] = [];
  /** “定位到某次停留”的高亮光圈（ADR-0017） */
  private focusMarker: AmapMarker | null = null;
  private groupByRecord = new Map<number, MapMarkerGroup>();
  private range: TravelRangeRequest = { kind: 'all' };
  private loadedKey = '';
  private fetchSeq = 0;
  /** 已发起过派生逆地理的记录 id（去重记忆 + 过期令牌：异步回来后不匹配就丢弃结果） */
  private lastRequestedIds = '';

  ngAfterViewInit(): void {
    this.initMap();
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
    return `${this.range.kind}|${this.range.from ?? ''}|${this.range.to ?? ''}|${this.range.favoriteOnly === true}|${this.page()}`;
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
        this.range.favoriteOnly === true,
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

  // ---------- 标注渲染（城市优先，无 City 回退坐标合并：ADR-0017 / ADR-0004） ----------

  /**
   * @param preserveView 为 true 时不关气泡、也不重置视野：异步派生回来的重渲染不应抢用户的视口/关掉已打开的信息窗（ADR-0017）
   */
  private renderMarkers(preserveView = false): void {
    const map = this.map;
    const amap = this.amap;
    if (!map || !amap) return;

    for (const marker of this.markers) map.remove(marker);
    this.markers = [];
    this.clearFocusRing();
    this.groupByRecord.clear();
    if (!preserveView) this.infoWindow?.close();

    // 无 City 快照的记录先按坐标派生城市（异步，不阻塞首帧），结果回来后再重渲染
    void this.requestDerivedCities();

    for (const group of groupMapMarkers(this.records(), this.derivedCities())) {
      for (const record of group.records) this.groupByRecord.set(record.id, group);

      const position = this.positionOf(group);
      const count = group.records.length;
      const ongoing = group.records.some((r) => r.departedAt === null);
      const favorite = group.records.some((r) => r.isFavorite);
      const label = group.kind === 'city' ? group.city! : group.records[0]!.locationName;

      const content = document.createElement('div');
      content.className = `tm-marker tm-marker--${ongoing ? 'ongoing' : 'ordinary'}`;
      content.setAttribute(
        'aria-label',
        `${label}：${count} 次停留${ongoing ? '（含进行中）' : ''}${favorite ? '（含精选收藏）' : ''}`,
      );

      // ADR-0017：城市标记 = 该城**首次到访**那条记录的解析结果（显式 iconKey > Regional icon）；
      // 地点标记仍要求组内解析一致（ADR-0016），否则回退默认标记（熊猫）+ 计数
      const icon = this.iconForGroup(group);
      if (icon) {
        content.classList.add('tm-marker--icon');
        const iconHolder = document.createElement('span');
        iconHolder.className = 'tm-marker__icon';
        iconHolder.innerHTML = travelIconMarkup(icon, 42);
        content.appendChild(iconHolder);
      } else {
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
      }

      if (favorite) {
        const star = document.createElement('span');
        star.className = 'tm-marker__star';
        star.textContent = '★';
        star.setAttribute('aria-hidden', 'true');
        content.appendChild(star);
      }

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
        // ADR-0017：城市标记压在「地点标记」之上——质心可能与该城某次停留（或无 City 的旧记录）重合，
        // 聚合口径优先可点；进行中的标记再抬一档
        zIndex: (group.kind === 'city' ? 1200 : 600) + (ongoing ? 200 : 0),
        title: label,
      });
      marker.on('click', () => this.openInfo(group));
      map.add(marker);

      this.markers.push(marker);
    }
  }

  /**
   * 无 City 快照的记录按坐标派生城市（ADR-0017）：只影响分组，不写库。
   * 先按快照渲染，派生结果到达后重渲染标记；同一批记录只发起一次。
   */
  private async requestDerivedCities(): Promise<void> {
    const pending = this.records().filter((record) => !record.city?.trim());
    if (pending.length === 0) {
      this.lastRequestedIds = '';
      return;
    }

    // 排序后再拼 id：同一批记录不管顺序如何都只算一次（避免多余的重渲染）
    const ids = pending.map((record) => record.id).sort((a, b) => a - b);
    const key = ids.join(',');
    if (key === this.lastRequestedIds) return;
    this.lastRequestedIds = key;

    const cities = await this.reverseGeocode.resolveCities(
      pending.map((record) => ({ lng: record.longitude, lat: record.latitude })),
    );
    // 期间翻页/换筛选 → 丢弃过期结果
    if (this.lastRequestedIds !== key) return;

    const byRecord = new Map<number, string>();
    pending.forEach((record, index) => {
      const city = cities[index];
      if (city) byRecord.set(record.id, city);
    });

    // 分组结果没变就不重渲染：避免异步回来时把用户已打开的气泡/已缩放的视野重置
    const before = this.markerSignature(this.derivedCities());
    const after = this.markerSignature(byRecord);
    this.derivedCities.set(byRecord);
    if (before === after) return;

    // 用户已经选过某条记录/打开过气泡时不再抢视口
    const engaged = this.selectedRecordId() !== null;
    this.renderMarkers(engaged);
    if (!engaged) this.fitView();
  }

  /** 标记集合签名（kind + 城市名 + 组内 id）：相同则不需要重渲染 */
  private markerSignature(derivedCities: ReadonlyMap<number, string>): string {
    return groupMapMarkers(this.records(), derivedCities)
      .map((group) => `${group.key}[${group.records.map((r) => r.id).join('|')}]`)
      .join(';');
  }

  /**
   * 城市标记取该城**首条记录**（首次到访）的图标；地点标记要求组内一致（ADR-0017 / 0016）。
   * 图标解析用**有效城市**：快照优先，派生城市补空缺（否则无快照的旧记录拿不到地区特色图标）。
   */
  private iconForGroup(group: MapMarkerGroup) {
    if (group.kind === 'city') {
      const source = iconSourceOf(group);
      return iconForRecord({
        iconKey: source.iconKey,
        city: effectiveCity(source, this.derivedCities()) ?? group.city,
      });
    }
    return sharedTravelIcon(group.records);
  }

  /** 组锚点（WGS-84 → GCJ-02） */
  private positionOf(group: MapMarkerGroup): [number, number] {
    const [lng, lat] = wgs84ToGcj02(group.lng, group.lat);
    return [lng, lat];
  }

  /** 单条记录的坐标（WGS-84 → GCJ-02）：定位到具体一次停留时用 */
  private positionOfRecord(record: TravelRecord): [number, number] {
    const [lng, lat] = wgs84ToGcj02(record.longitude, record.latitude);
    return [lng, lat];
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

  private openInfo(group: MapMarkerGroup, anchor?: readonly [number, number]): void {
    if (!this.infoWindow) return;
    this.infoWindow.setContent(this.buildInfoContent(group));
    this.infoWindow.open(this.map!, [...(anchor ?? this.positionOf(group))]);
  }

  /** “定位到某次停留”的高亮光圈（ADR-0017）：城市标记只能标到质心，具体记录用光圈指出 */
  private showFocusRing(position: readonly [number, number]): void {
    const amap = this.amap;
    const map = this.map;
    if (!amap || !map) return;
    this.clearFocusRing();
    const content = document.createElement('span');
    content.className = 'tm-focus-ring';
    const marker = new amap.Marker({
      position: [...position],
      content,
      offset: new amap.Pixel(0, 0),
      zIndex: 2000,
    });
    map.add(marker);
    this.focusMarker = marker;
  }

  private clearFocusRing(): void {
    if (!this.focusMarker) return;
    this.map?.remove(this.focusMarker);
    this.focusMarker = null;
  }

  private buildInfoContent(group: MapMarkerGroup): HTMLElement {
    const root = document.createElement('div');
    root.className = 'tm-info';

    const isCity = group.kind === 'city';
    const primary = group.records[0]!;
    const head = document.createElement('div');
    head.className = 'tm-info__title';
    const title = isCity ? (group.city ?? primary.locationName) : primary.locationName;
    head.textContent = primary.isFavorite ? `★ ${title}` : title;
    root.appendChild(head);

    const sub = document.createElement('p');
    sub.className = 'tm-info__sub';
    sub.textContent =
      group.records.length > 1
        ? isCity
          ? `${group.records.length} 次停留 · 该城市；标记在该城各次停留的中心`
          : `${group.records.length} 次停留 · 同一地点`
        : `坐标 ${group.lat.toFixed(5)}, ${group.lng.toFixed(5)}（WGS-84）`;
    root.appendChild(sub);

    const list = document.createElement('div');
    list.className = 'tm-info__list';
    for (const record of group.records) {
      // 一行 = 一条停留：左侧是信息（点它=在地图上定位到这次停留），右侧是该条的快捷操作
      const row = document.createElement('div');
      row.className = 'tm-info__row';

      const main = document.createElement('button');
      main.type = 'button';
      main.className = 'tm-info__row-main';
      main.title = '在地图上定位到这次停留';
      main.setAttribute('aria-label', `在地图上定位到 ${record.locationName}`);

      const line = document.createElement('span');
      line.className = 'tm-info__row-line';
      if (record.isFavorite) {
        const star = document.createElement('span');
        star.className = 'tm-info__star';
        star.textContent = '★ ';
        line.appendChild(star);
      }
      // 城市标记下同城地点不同，行内带上地点名
      if (isCity) {
        const place = document.createElement('span');
        place.className = 'tm-info__row-place';
        place.textContent = record.locationName;
        line.appendChild(place);
      }
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
      main.appendChild(line);
      main.addEventListener('click', () => this.focusOnMap(record.id, FOCUS_ZOOM));

      const actions = document.createElement('span');
      actions.className = 'tm-info__row-actions';

      const detail = document.createElement('button');
      detail.type = 'button';
      detail.className = 'row-action';
      detail.textContent = '详情';
      detail.title = '查看这条停留的详情';
      detail.setAttribute('aria-label', `查看 ${record.locationName} 的详情`);
      detail.addEventListener('click', () => {
        this.infoWindow?.close();
        void this.router.navigate(['/travels', record.id]);
      });

      const again = document.createElement('button');
      again.type = 'button';
      again.className = 'row-action row-action--primary';
      again.textContent = '再来';
      again.title = '再来一次（预填这个地点新建）';
      again.setAttribute('aria-label', `再来一次（${record.locationName}）`);
      again.addEventListener('click', () => {
        this.infoWindow?.close();
        void this.router.navigate(['/travels/new'], {
          state: {
            prefill: {
              locationName: record.locationName,
              longitude: record.longitude,
              latitude: record.latitude,
              // 无快照时把派生城市一并带入：新记录因此拿到城市快照（ADR-0017）
              city: effectiveCity(record, this.derivedCities()) ?? null,
              iconKey: record.iconKey ?? null,
            },
          },
        });
      });

      actions.append(detail, again);
      row.append(main, actions);
      list.appendChild(row);
    }
    root.appendChild(list);
    return root;
  }

  /** 侧栏选中记录：居中到该条停留的坐标并弹信息窗（保底 FOCUS_ZOOM）。 */
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
    const record = this.records().find((item) => item.id === recordId);
    const map = this.map;
    if (!map || !group || !record) return;
    // ADR-0017：城市标记只是一城一标，定位具体一次停留要落到该记录自己的坐标，并打高亮光圈
    const position = this.positionOfRecord(record);
    map.setCenter([...position]);
    if (map.getZoom() < minZoom) map.setZoom(minZoom);
    this.showFocusRing(position);
    this.openInfo(group, position);
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
