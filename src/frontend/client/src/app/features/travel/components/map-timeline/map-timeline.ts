import { ChangeDetectionStrategy, Component, OnDestroy, computed, effect, inject, input, output, signal } from '@angular/core';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelRecord } from '../../models/travel-record.model';
import { buildCityRuns, newestOf } from '../../utils/timeline.util';
import type { TimelineRun } from '../../utils/timeline.util';
import { fmtDuration, fmtRange } from '../../utils/travel-display';

/**
 * 地图时光轴（Map timeline，ADR-0015）：
 * 地图上方一条横向时间视图。按 City snapshot 把“同城连续停留”折叠为一个节点（City run），
 * 无 City 的旧记录各成独立节点。节点显示：上方城市名（单段加日期），下方该段首图缩略图（若有）。
 * 鼠标悬停节点显示动作气泡：详情（run 的最新一条）/ 展开|收起（N>1）/ 删除（确认后整体移入回收站）。
 */
@Component({
  selector: 'app-map-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
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

  private readonly requestedThumbs = new Set<string>();
  private readonly objectUrls = new Set<string>();

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
  }

  ngOnDestroy(): void {
    for (const url of this.objectUrls) URL.revokeObjectURL(url);
  }

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

  protected toggleExpanded(key: string): void {
    const next = new Set(this.expanded());
    if (next.has(key)) next.delete(key);
    else next.add(key);
    this.expanded.set(next);
  }

  protected onHover(key: string | null): void {
    this.hovered.set(key);
  }

  protected openDetail(run: TimelineRun): void {
    this.detail.emit(newestOf(run).id);
  }

  protected focusRecord(id: number): void {
    this.focus.emit(id);
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
}
