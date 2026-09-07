import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelImage, TravelRecord } from '../../models/travel-record.model';

interface DisplayImage extends TravelImage {
  thumbnailSrc: string;
  originalSrc?: string;
}

@Component({
  selector: 'app-travel-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink],
  templateUrl: './travel-detail-page.html',
  styleUrl: './travel-detail-page.scss',
})
export class TravelDetailPage implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly travel = inject(TravelHistoryService);
  private readonly objectUrls = new Set<string>();

  protected readonly record = signal<TravelRecord | null>(null);
  protected readonly images = signal<DisplayImage[]>([]);
  protected readonly selectedImage = signal<DisplayImage | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  async ngOnInit(): Promise<void> {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!Number.isInteger(id)) {
      this.error.set('旅游记录不存在。');
      this.loading.set(false);
      return;
    }
    try {
      const record = await this.travel.getById(id);
      const images = await this.travel.getImages(id);
      this.record.set(record);
      const displayImages: DisplayImage[] = [];
      for (const image of images) {
        displayImages.push({ ...image, thumbnailSrc: await this.toObjectUrl(image.thumbnailUrl) });
      }
      this.images.set(displayImages);
    } catch {
      this.error.set('加载旅游详情失败，请稍后重试。');
    } finally {
      this.loading.set(false);
    }
  }

  ngOnDestroy(): void {
    for (const url of this.objectUrls) URL.revokeObjectURL(url);
  }

  protected async openOriginal(image: DisplayImage): Promise<void> {
    if (!image.originalSrc) {
      image.originalSrc = await this.toObjectUrl(image.originalUrl);
      this.images.update(items => [...items]);
    }
    this.selectedImage.set(image);
  }

  protected closeOriginal(): void {
    this.selectedImage.set(null);
  }

  private async toObjectUrl(path: string): Promise<string> {
    const blob = await this.travel.getMediaBlob(path);
    const url = URL.createObjectURL(blob);
    this.objectUrls.add(url);
    return url;
  }
}
