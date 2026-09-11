import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, computed, effect, inject, input, output, signal } from '@angular/core';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelRecord } from '../../models/travel-record.model';
import { buildCityRuns, newestOf } from '../../utils/timeline.util';
import type { TimelineRun } from '../../utils/timeline.util';
import { fmtDuration, fmtRange } from '../../utils/travel-display';
import { iconForRecord } from '../../utils/travel-icon.util';
import { TravelIconComponent } from '../travel-icon/travel-icon';

/**
 * 地图时光轴（Map timeline，ADR-0015）：
 * 地图上方一条横向时间视图。按 City snapshot 把“同城连续停留”折叠为一个节点（City run），
 * 无 City 的旧记录各成独立节点。节点按奇偶上下镜像：偶数节点(第 2/4…)城市名在上、缩略图在下；
 * 奇数节点(第 1/3…)缩略图在上、城市名在下。
 * 悬停(桌面)/点击(触屏)节点时圆点轻微放大并浮现“节点操作条”：详情（run 最新一条）/
 * 展开|收起（N>1）/ 删除（确认后整段移入回收站）；点击圆点本体触发 focus，宿主在地图上
 * 定位到 run 最新一条的地点（保底 zoom 见 map-page 的 TIMELINE_FOCUS_ZOOM）。
 * 展开后每一行＝"在地图上定位该条" + 行内「详情」（直接进该次停留的详情页）。
 */
@Component({
  selector: 'app-map-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TravelIconComponent],
  templateUrl: './map-timeline.html',
  styleUrl: './map-timeline.scss',
})
export class MapTimeline implements OnDestroy {
  private readonly travel = inject(TravelHistoryService);

  /** 当前（已过滤）记录；顺序不限，组件内按到达时间升序切分 City run */
  readonly records = input<TravelRecord[]>([]);

  /** 打开某条记录详情页 */
  readonly detail = output<number>();
  /** 聚焦某条记录（地图信息窗 + 侧栏高亮） */
  readonly focus = output<number>();
  /** 请求删除整个 City run（宿主负责确认后逐条移入回收站） */
  readonly deleteRun = output<TimelineRun>();

  readonly runs = computed(() => buildCityRuns(this.records()));
  readonly thumbs = signal(new Map<string, string>());
  readonly expanded = signal(new Set<string>());
  protected readonly hovered = signal<string | null>(null);
  /** 触屏/点击固定的操作条：点节点外部或再次点该节点时收起 */
  protected readonly pinned = signal<string | null>(null);

  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly requestedThumbs = new Set<string>();
  private readonly objectUrls = new Set<string>();
  private readonly removeDocListener = (): void => {
    document.removeEventListener('pointerdown', this.onDocPointerDown, true);
  };

  constructor() {
    effect(() => {
      const runs = this.runs();
      for (const run of runs) {
        if (!this.requestedThumbs.has(run.key)) {
          this.requestedThumbs.add(run.key);
          void this.loadThumb(run);
        }
      }
    });
    // 触屏固定态：点组件外部任意处即收起
    document.addEventListener('pointerdown', this.onDocPointerDown, true);
  }

  ngOnDestroy(): void {
    this.removeDocListener();
    for (const url of this.objectUrls) URL.revokeObjectURL(url);
  }

  private readonly onDocPointerDown = (event: PointerEvent): void => {
    if (this.pinned() !== null && !this.host.nativeElement.contains(event.target as Node)) {
      this.pinned.set(null);
    }
  };

  private async loadThumb(run: TimelineRun): Promise<void> {
    // 取 run 中“第一张有图的记录”的首图缩略图；否则无缩略图
    for (const record of run.records) {
      try {
        const images = await this.travel.getImages(record.id);
        if (images.length > 0) {
          const url = await this.mediaUrl(images[0].thumbnailUrl);
          this.thumbs.update((map) => new Map(map).set(run.key, url));
          return;
        }
      } catch {
        // 单条图片拉取失败不阻塞其余 run
      }
    }
  }

  private async mediaUrl(path: string): Promise<string> {
    const blob = await this.travel.getMediaBlob(path);
    const url = URL.createObjectURL(blob);
    this.objectUrls.add(url);
    return url;
  }

  protected thumbOf(run: TimelineRun): string | undefined {
    return this.thumbs().get(run.key);
  }

  protected isExpanded(key: string): boolean {
    return this.expanded().has(key);
  }

  /** 互斥展开：同一时刻只展开一个 City run；再次点击已展开的则收起 */
  protected toggleExpanded(key: string): void {
    this.expanded.update((current) => {
      if (current.has(key)) return new Set(); // 收起自身
      return new Set([key]); // 只保留当前节点，其它自动收起
    });
  }

  protected onHover(key: string | null): void {
    this.hovered.set(key);
  }

  /** 操作条是否打开：桌面悬停或触屏点击固定，二者任一即可 */
  protected menuOpen(key: string): boolean {
    return this.hovered() === key || this.pinned() === key;
  }

  /** 触屏：点击节点空白区（非按钮/圆点）切换固定操作条；桌面由 hover 接管 */
  protected onColTap(event: Event, key: string): void {
    if (this.isMouseEvent(event)) return;
    this.pinned.update((current) => (current === key ? null : key));
  }

  /** 点击圆点本体：地图定位 run 最新一条；触屏顺手固定操作条 */
  protected onDotPress(event: Event, run: TimelineRun): void {
    event.stopPropagation();
    if (!this.isMouseEvent(event)) this.pinned.set(run.key);
    this.locateRun(run);
  }

  /** 鼠标事件（PointerEvent.pointerType === 'mouse'）一律视为桌面 hover 场景 */
  private isMouseEvent(event: Event): boolean {
    return !(event instanceof PointerEvent) || event.pointerType === 'mouse';
  }

  protected openDetail(run: TimelineRun): void {
    this.detail.emit(newestOf(run).id);
  }

  /** 展开后的每行都有「详情」：直接跳该次停留的详情页（与圆点定位、行内定位互不干扰） */
  protected openRecordDetail(record: TravelRecord): void {
    this.detail.emit(record.id);
  }

  protected focusRecord(id: number): void {
    this.focus.emit(id);
  }

  /** 点击节点锚点圆（圆身，非气泡）：定位该 run 最新一条到地图（与「详情」同口径 newestOf）。 */
  protected locateRun(run: TimelineRun): void {
    this.focus.emit(newestOf(run).id);
  }

  protected removeRun(run: TimelineRun): void {
    this.deleteRun.emit(run);
  }

  // —— 模板辅助 ——
  protected rangeOf(record: TravelRecord): string {
    return fmtRange(record);
  }

  protected durationOf(record: TravelRecord): string {
    return fmtDuration(record);
  }

  protected labelOf(run: TimelineRun): string {
    return run.records.length > 1 ? `${run.label} · ${run.records.length} 段` : run.label;
  }

  /** 该节点是否含精选收藏（ADR-0014）：模板据此在标签前渲染 ★ 徽标 */
  protected hasFavorite(run: TimelineRun): boolean {
    return run.records.some((record) => record.isFavorite);
  }

  /**
   * 节点旅行标识图标（ADR-0016）：与「详情 / 定位」同口径取该节点最新一条记录（newestOf），
   * 这样同一城市的多次到访也总能显示标识，不因组内不一致而整组丢失图标。
   */
  protected iconOf(run: TimelineRun) {
    return iconForRecord(newestOf(run));
  }
}
