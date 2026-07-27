import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * 通用加载态组件。
 */
@Component({
  selector: 'app-loading-spinner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './loading-spinner.html',
  styleUrl: './loading-spinner.scss',
})
export class LoadingSpinner {
  readonly message = input<string>('加载中…');
}
