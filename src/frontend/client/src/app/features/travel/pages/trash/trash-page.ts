import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelRecord } from '../../models/travel-record.model';
import { fmtDateTime } from '../../utils/travel-display';

/**
 * 回收站（ADR-0013）：列出已软删除的记录，支持“恢复”与“彻底删除”。
 * 彻底删除会连同图片与媒体一并清理，不可恢复，需二次确认。
 */
@Component({
  selector: 'app-trash-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './trash-page.html',
  styleUrl: './trash-page.scss',
})
export class TrashPage implements OnInit {
  private readonly travel = inject(TravelHistoryService);

  readonly loading = signal(true);
  readonly error = signal('');
  readonly items = signal<TravelRecord[]>([]);
  readonly busyId = signal<number | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      this.items.set(await this.travel.getAllTrash());
    } catch {
      this.error.set('回收站加载失败，请稍后重试。');
    } finally {
      this.loading.set(false);
    }
  }

  protected fmt(iso: string): string {
    return fmtDateTime(iso);
  }

  protected async restore(record: TravelRecord): Promise<void> {
    if (this.busyId() !== null) return;
    this.error.set('');
    this.busyId.set(record.id);
    try {
      await this.travel.restore(record.id);
      this.items.update((items) => items.filter((item) => item.id !== record.id));
    } catch {
      this.error.set('恢复失败，请稍后重试。');
    } finally {
      this.busyId.set(null);
    }
  }

  protected async purge(record: TravelRecord): Promise<void> {
    if (this.busyId() !== null) return;
    const confirmed = window.confirm(
      `确定要彻底删除“${record.locationName}”这条记录吗？描述、图片及相关数据将一并删除，且不可恢复。`,
    );
    if (!confirmed) return;
    this.error.set('');
    this.busyId.set(record.id);
    try {
      await this.travel.deletePermanently(record.id);
      this.items.update((items) => items.filter((item) => item.id !== record.id));
    } catch {
      this.error.set('彻底删除失败，请稍后重试。');
    } finally {
      this.busyId.set(null);
    }
  }
}
