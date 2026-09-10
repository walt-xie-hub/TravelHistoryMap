import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import type { TravelIcon } from '../../utils/travel-icon.util';
import { travelIconMarkup } from '../../utils/travel-icon.util';

/**
 * 旅行标识图标（ADR-0016）：把目录里的剪影渲染成内联 SVG。
 *
 * - 透明：除形体以外没有底衬、没有文字；颜色取 `currentColor`（由使用处 CSS 决定）。
 * - 标记来自本仓库静态目录（无用户输入），因此走 `bypassSecurityTrustHtml` 直出，
 *   与地图标记共用 `travelIconMarkup`，保证两种渲染路径完全一致。
 */
@Component({
  selector: 'app-travel-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<span class="travel-icon" [innerHTML]="markup()"></span>',
  styles: `
    :host {
      display: inline-flex;
      line-height: 0;
      color: inherit;
    }

    .travel-icon {
      display: inline-flex;
    }
  `,
})
export class TravelIconComponent {
  private readonly sanitizer = inject(DomSanitizer);

  readonly icon = input<TravelIcon | null | undefined>(null);
  /** 边长（px） */
  readonly size = input(22);

  protected readonly markup = computed(() => {
    const icon = this.icon();
    return icon ? this.sanitizer.bypassSecurityTrustHtml(travelIconMarkup(icon, this.size())) : '';
  });
}
