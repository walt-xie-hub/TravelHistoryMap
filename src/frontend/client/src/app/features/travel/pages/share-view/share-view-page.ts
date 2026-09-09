import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DomSanitizer } from '@angular/platform-browser';
import type { SafeHtml } from '@angular/platform-browser';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { PublicShareSnapshot } from '../../models/travel-record.model';
import { sanitizeRichTextToTrusted } from '../../utils/rich-text.util';

@Component({
  selector: 'app-share-view-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink],
  templateUrl: './share-view-page.html',
  styleUrl: './share-view-page.scss',
})
export class ShareViewPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly travel = inject(TravelHistoryService);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly snapshot = signal<PublicShareSnapshot | null>(null);

  async ngOnInit(): Promise<void> {
    const token = this.route.snapshot.paramMap.get('token') ?? '';
    try {
      this.snapshot.set(await this.travel.getShareSnapshot(token));
    } catch {
      this.error.set('分享不存在或已被删除。');
    } finally {
      this.loading.set(false);
    }
  }

  protected safeHtml(html: string): SafeHtml {
    return sanitizeRichTextToTrusted(this.sanitizer, html);
  }
}
