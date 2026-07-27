import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PageHeader } from '@shared/components/page-header/page-header';
import { EmptyState } from '@shared/components/empty-state/empty-state';

@Component({
  selector: 'app-map-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState],
  templateUrl: './map-page.html',
  styleUrl: './map-page.scss',
})
export class MapPage {}
