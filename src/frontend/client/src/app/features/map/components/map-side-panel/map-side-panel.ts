import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal } from '@angular/core';
import { LoadingSpinner } from '@shared/components/loading-spinner/loading-spinner';
import type {
  TravelRangeKind,
  TravelRangeRequest,
  TravelRecord,
} from '../../models/travel-record.model';
import { fmtDuration, fmtRange } from '../../utils/travel-display';

export interface MapUserOption {
  id: number;
  name: string;
  email: string;
}

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
 * 地图页侧边时间线面板：用户选择（记忆于 localStorage）、停留统计、
 * 时间范围筛选 chips（全部/近30天/今年/自定义 → 后端 from/to）与按到达时间倒序的记录列表。
 * 展示决策见 docs/adr/0004。
 */
@Component({
  selector: 'app-map-side-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LoadingSpinner],
  templateUrl: './map-side-panel.html',
  styleUrl: './map-side-panel.scss',
})
export class MapSidePanel {
  readonly users = input<MapUserOption[]>([]);
  readonly usersLoading = input(false);
  readonly selectedUserId = input<number | null>(null);
  readonly records = input<TravelRecord[]>([]);
  readonly travelLoading = input(false);
  readonly selectedRecordId = input<number | null>(null);

  readonly userChange = output<number>();
  readonly filterChange = output<TravelRangeRequest>();
  readonly recordSelect = output<number>();

  readonly kind = signal<TravelRangeKind>('all');
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

  onUserSelect(event: Event): void {
    const id = Number((event.target as HTMLSelectElement).value);
    if (Number.isFinite(id) && id > 0) this.userChange.emit(id);
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

  private emitFilter(): void {
    const request = this.buildRequest();
    if (request) this.filterChange.emit(request);
  }

  private buildRequest(): TravelRangeRequest | null {
    const kind = this.kind();
    if (kind === 'all') return { kind };

    const now = Date.now();
    if (kind === 'last30') {
      return { kind, from: new Date(now - 30 * DAY_MS).toISOString(), to: new Date(now).toISOString() };
    }
    if (kind === 'year') {
      const startOfYear = new Date(new Date().getFullYear(), 0, 1);
      return { kind, from: startOfYear.toISOString(), to: new Date(now).toISOString() };
    }
    const from = localDayToIso(this.customFrom(), false);
    const to = localDayToIso(this.customTo(), true);
    return from && to ? { kind, from, to } : null;
  }
}
