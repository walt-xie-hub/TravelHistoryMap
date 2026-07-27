import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * 通用页头组件：展示页面标题与副标题，并通过内容投影放置操作区。
 */
@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-header.html',
  styleUrl: './page-header.scss',
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string>('');
}
