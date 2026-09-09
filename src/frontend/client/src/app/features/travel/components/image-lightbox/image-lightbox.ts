/**
 * 可复用全屏图片灯箱（M4 首期）。
 *
 * - 输入一组“懒加载原图”条目（items）+ 起始索引；打开后按需加载当前原图。
 * - 支持：左右切换按钮、键盘 ←/→/Esc、触摸横向滑动（指针事件）、右上角计数、底部可选动作。
 * - 纯展示组件，不持有数据；原图由宿主提供 load()（通常是用带 token 的 blob → 对象 URL）。
 */
import { ChangeDetectionStrategy, Component, ElementRef, HostListener, AfterViewInit, effect, input, output, signal, untracked, viewChild } from '@angular/core';

export interface LightboxItem {
  /** 稳定键（Angular track 用） */
  key: string | number;
  /** 标题 / 文件名，显示在顶栏 */
  title: string;
  /** 懒加载原图：resolve 为可直接 <img> 的地址（对象 URL / 完整 URL）。 */
  load: () => Promise<string>;
}

@Component({
  selector: 'app-image-lightbox',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './image-lightbox.html',
  styleUrl: './image-lightbox.scss',
})
export class ImageLightbox implements AfterViewInit {
  readonly items = input<LightboxItem[]>([]);
  readonly startIndex = input(0);
  /** 可选的顶栏动作（如“查看旅行详情”） */
  readonly actionLabel = input('');
  readonly action = input<(() => void) | null>(null);
  readonly close = output<void>();

  private readonly root = viewChild<ElementRef<HTMLDivElement>>('root');

  protected readonly index = signal(0);
  protected readonly src = signal('');
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  /** 触摸滑动起点 X */
  private pointerX = 0;

  constructor() {
    // 仅在 items/startIndex（来自宿主）变化时定位；内部的 index/loadCurrent 用 untracked 包裹，
    // 避免内部 index 变化又触发本 effect 把索引回跳到起始位。
    effect(() => {
      const items = this.items();
      const total = items.length;
      const requested = this.startIndex();
      const start = total === 0 ? 0 : Math.min(Math.max(requested, 0), total - 1);
      untracked(() => {
        this.index.set(start);
        void this.loadCurrent();
      });
    });
  }

  protected async loadCurrent(): Promise<void> {
    const items = this.items();
    const item = items[this.index()];
    if (!item) {
      this.src.set('');
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.error.set('');
    try {
      this.src.set(await item.load());
    } catch {
      this.error.set('图片加载失败。');
    } finally {
      this.loading.set(false);
    }
  }

  protected prev(): void {
    this.go(-1);
  }

  protected next(): void {
    this.go(1);
  }

  private go(delta: number): void {
    const total = this.items().length;
    if (total === 0) return;
    const next = (this.index() + delta + total) % total;
    this.index.set(next);
    void this.loadCurrent();
  }

  /** 灯箱打开期间全局监听键盘：Esc 关闭、←/→ 切换。 */
  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.close.emit();
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      this.prev();
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      this.next();
    }
  }

  /** 打开后把焦点放进弹层，保证物理方向键/Esc 都落在文档内并冒泡到本监听。 */
  ngAfterViewInit(): void {
    this.root()?.nativeElement.focus();
  }

  protected onPointerDown(event: PointerEvent): void {
    this.pointerX = event.clientX;
  }

  protected onPointerUp(event: PointerEvent): void {
    const deltaX = event.clientX - this.pointerX;
    if (Math.abs(deltaX) > 48) {
      if (deltaX < 0) this.next();
      else this.prev();
    }
  }

  protected runAction(): void {
    const run = this.action();
    if (run) run();
  }
}
