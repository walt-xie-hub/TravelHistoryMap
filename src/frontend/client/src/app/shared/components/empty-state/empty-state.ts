import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * 通用空状态组件：用于列表无数据时的占位提示。
 */
@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './empty-state.html',
  styleUrl: './empty-state.scss',
})
export class EmptyState {
  readonly icon = input<string>('🗺️');
  readonly title = input<string>('暂无数据');
  readonly description = input<string>('');
}
