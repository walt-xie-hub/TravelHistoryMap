import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelRecord } from '../../models/travel-record.model';
import {
  buildShareText,
  canvasToPngUrl,
  renderShareCard,
  toCardRows,
} from '../../share/share-card';
import { PostcardEditor } from '../postcard-editor/postcard-editor';

/**
 * 分享弹层（M1/达人分享 + ADR-0018 明信片）：
 * ① 足迹分享卡：canvas 合成 → 下载 PNG / 复制文案（纯本地）；
 * ② 明信片：10 款模板 + 可选照片与模块，编辑器里改内容 → 导出图片（纯本地，ADR-0018）；
 * ③ 只读分享链接：调 POST /api/travels/share 生成 token → 复制 /s/{token} 链接。
 *
 * 两种图片形态共享同一个弹层（不新开路由），默认仍是足迹卡——它信息密度高、看一眼就懂。
 */
@Component({
  selector: 'app-travel-share-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PostcardEditor],
  templateUrl: './travel-share-dialog.html',
  styleUrl: './travel-share-dialog.scss',
})
export class TravelShareDialog {
  readonly records = input<TravelRecord[]>([]);
  readonly close = output<void>();

  private readonly travel = inject(TravelHistoryService);

  /** 图片形态：足迹卡（默认）/ 明信片 */
  protected readonly mode = signal<'card' | 'postcard'>('card');

  protected readonly cardUrl = signal('');
  protected readonly caption = signal('');
  protected readonly title = signal('');
  protected readonly busy = signal(false);
  protected readonly link = signal('');
  protected readonly error = signal('');
  protected readonly copied = signal('');

  constructor() {
    effect(() => {
      const records = this.records();
      if (!records.length) return;
      const sorted = [...records].sort((a, b) => a.arrivedAt.localeCompare(b.arrivedAt));
      this.title.set(
        sorted.length === 1
          ? sorted[0]!.locationName
          : `${sorted[0]!.locationName} 等 ${sorted.length} 站`,
      );
      const input = { title: this.title(), rows: toCardRows(sorted) };
      this.caption.set(buildShareText(input));
      if (!this.cardUrl()) {
        this.cardUrl.set(canvasToPngUrl(renderShareCard(input)));
      }
    });
  }

  protected download(): void {
    const url = this.cardUrl();
    if (!url) return;
    const a = document.createElement('a');
    a.href = url;
    a.download = `travel-map-${this.title().replace(/\s+/g, '-') || 'footprint'}.png`;
    document.body.appendChild(a);
    a.click();
    a.remove();
  }

  protected async copyCaption(): Promise<void> {
    await this.copy(this.caption(), '文案已复制');
  }

  protected async generateLink(): Promise<void> {
    const ids = this.records().map((r) => r.id);
    if (!ids.length) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const created = await this.travel.shareTravels(ids);
      this.link.set(`${window.location.origin}${created.url}`);
    } catch {
      this.error.set('生成分享链接失败，请稍后重试。');
    } finally {
      this.busy.set(false);
    }
  }

  protected async copyLink(): Promise<void> {
    await this.copy(this.link(), '链接已复制');
  }

  private async copy(text: string, okLabel: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.copied.set(okLabel);
      setTimeout(() => this.copied.set(''), 1600);
    } catch {
      this.error.set('复制失败，请手动复制。');
    }
  }
}
