import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { LoadingSpinner } from '@shared/components/loading-spinner/loading-spinner';
import type {
  TravelRangeKind,
  TravelRangeRequest,
  TravelRecord,
} from '../../models/travel-record.model';
import { fmtDuration, fmtRange } from '../../utils/travel-display';
import { effectiveCity } from '../../utils/travel-display';
import { iconForRecord } from '../../utils/travel-icon.util';
import { TravelIconComponent } from '../travel-icon/travel-icon';

interface TravelRow {
  record: TravelRecord;
  rangeLabel: string;
  durationLabel: string;
}

const DAY_MS = 86_400_000;
const STORAGE_NONE = '';

/** 本地某天的 UTC 起止（用于自定义日期范围请求） */
function localDayToIso(dateText: string, atEndOfDay: boolean): string {
  const [y, m, d] = dateText.split('-').map((part) => Number(part));
  const date = new Date(y!, m! - 1, d!, atEndOfDay ? 23 : 0, atEndOfDay ? 59 : 0, atEndOfDay ? 59 : 0);
  return Number.isNaN(date.getTime()) ? '' : date.toISOString();
}

/**
 * 地图页侧边时间线面板：停留统计、时间范围筛选 chips
 * （全部/近30天/今年/自定义 → 后端 from/to）与按到达时间倒序的记录列表。
 * ADR-0005：记录归属=当前登录用户（JWT），面板不再提供用户选择。
 * 展示决策见 docs/adr/0004。
 */
@Component({
  selector: 'app-map-side-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LoadingSpinner, RouterLink, TravelIconComponent],
  templateUrl: './map-side-panel.html',
  styleUrl: './map-side-panel.scss',
})
export class MapSidePanel {
  private readonly router = inject(Router);

  readonly records = input<TravelRecord[]>([]);
  /** 无 City 快照记录的派生城市（recordId → 城市）：与地图同一口径（ADR-0017） */
  readonly derivedCities = input<ReadonlyMap<number, string>>(new Map());
  readonly travelLoading = input(false);
  readonly selectedRecordId = input<number | null>(null);
  readonly page = input(1);
  readonly totalPages = input(1);
  readonly totalCount = input(0);

  readonly filterChange = output<TravelRangeRequest>();
  readonly recordSelect = output<number>();
  readonly recordDetail = output<number>();
  readonly recordDelete = output<number>();
  readonly pageChange = output<number>();

  readonly kind = signal<TravelRangeKind>('all');
  /** 只看精选收藏（ADR-0014） */
  readonly favoriteOnly = signal(false);
  readonly customFrom = signal<string>(STORAGE_NONE);
  readonly customTo = signal<string>(STORAGE_NONE);

  readonly total = computed(() => this.records().length);
  readonly ongoingCount = computed(() => this.records().filter((r) => !r.departedAt).length);

  readonly rows = computed<TravelRow[]>(() =>
    this.records().map((record) => ({
      record,
      rangeLabel: fmtRange(record),
      durationLabel: fmtDuration(record),
    })),
  );

  readonly customComplete = computed(() => Boolean(this.customFrom() && this.customTo()));

  constructor() {
    // 联动：选中记录后，列表行滚动到可视区域并高亮
    effect(() => {
      const id = this.selectedRecordId();
      if (id === null) return;
      requestAnimationFrame(() => {
        document.getElementById(`tm-record-${id}`)?.scrollIntoView({ block: 'nearest' });
      });
    });
  }

  selectKind(kind: TravelRangeKind): void {
    this.kind.set(kind);
    if (kind !== 'custom' || this.customComplete()) this.emitFilter();
  }

  applyCustom(): void {
    if (!this.customComplete()) return;
    this.kind.set('custom');
    this.emitFilter();
  }

  clearCustom(): void {
    this.customFrom.set(STORAGE_NONE);
    this.customTo.set(STORAGE_NONE);
    if (this.kind() === 'custom') {
      this.kind.set('all');
      this.emitFilter();
    }
  }

  /** “★ 收藏”开关：只看精选收藏（与时间范围叠加）；自定义时间未填全时不生效（避免与已应用筛选脱节） */
  toggleFavoriteOnly(): void {
    this.favoriteOnly.update((value) => !value);
    const request = this.buildRequest();
    if (!request) {
      this.favoriteOnly.update((value) => !value);
      return;
    }
    this.filterChange.emit(request);
  }

  onCustomFrom(value: string): void {
    this.customFrom.set(value);
  }

  onCustomTo(value: string): void {
    this.customTo.set(value);
  }

  isKind(kind: TravelRangeKind): boolean {
    return this.kind() === kind;
  }

  onRecordSelect(id: number): void {
    this.recordSelect.emit(id);
  }

  /** 行内旅行标识图标（ADR-0016）：显式选择 > 城市特色；无快照时用地图派生的城市（ADR-0017） */
  protected iconOf(record: TravelRecord) {
    return iconForRecord({ iconKey: record.iconKey, city: effectiveCity(record, this.derivedCities()) });
  }

  onRecordDelete(event: Event, id: number): void {
    event.stopPropagation();
    this.recordDelete.emit(id);
  }

  onRecordDetail(event: Event, id: number): void {
    event.stopPropagation();
    this.recordDetail.emit(id);
  }

  /** “再来一次”：预填本地点（名称/坐标/城市/标识）跳新建页，到达=现在、离开留空 */
  onRecordAgain(event: Event, record: TravelRecord): void {
    event.stopPropagation();
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
  }

  /** 结束停留（进行中）：跳到详情页并进入“补记离开时间”编辑 */
  onRecordFinish(event: Event, record: TravelRecord): void {
    event.stopPropagation();
    void this.router.navigate(['/travels', record.id], { queryParams: { finish: '1' } });
  }

  onPageChange(page: number): void {
    this.pageChange.emit(page);
  }

  private emitFilter(): void {
    const request = this.buildRequest();
    if (request) this.filterChange.emit(request);
  }

  private buildRequest(): TravelRangeRequest | null {
    const kind = this.kind();
    const favoriteOnly = this.favoriteOnly();
    if (kind === 'all') return { kind, favoriteOnly };

    const now = Date.now();
    if (kind === 'last30') {
      return { kind, from: new Date(now - 30 * DAY_MS).toISOString(), to: new Date(now).toISOString(), favoriteOnly };
    }
    if (kind === 'year') {
      const startOfYear = new Date(new Date().getFullYear(), 0, 1);
      return { kind, from: startOfYear.toISOString(), to: new Date(now).toISOString(), favoriteOnly };
    }
    const from = localDayToIso(this.customFrom(), false);
    const to = localDayToIso(this.customTo(), true);
    return from && to ? { kind, from, to, favoriteOnly } : null;
  }
}
