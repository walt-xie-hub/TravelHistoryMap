import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelImage } from '../../models/travel-record.model';
import { fmtDateTime } from '../../utils/travel-display';

/** 照片墙里的单张：DTO + 可直接 <img> 的缩略图地址（resolveMediaUrl） */
interface WallImage {
  image: TravelImage;
  src: string;
}

/** 一段“带照片的旅行”在时间轴上的条目 */
interface WallTrip {
  id: number;
  locationName: string;
  arrivedAt: string;
  departedAt: string | null;
  /** 本地时区的年份标签（如 “2026”），用于年份筛选 */
  yearLabel: string;
  /** 本地友好日期，如 “8月20日 16:00” */
  timeLabel: string;
  /** 本地友好日期（离开），进行中为 null */
  endLabel: string | null;
  images: WallImage[];
}

type YearFilter = 'all' | string;

/**
 * 照片墙 / 时光轴（M3 首期，纯只读）：
 * 把当前登录用户各段旅行里带照片的足迹，按到达时间倒序排成一条时间轴，
 * 每段展示地点、时间与缩略图墙；点任意缩略图跳到该旅行详情页看原图/灯箱。
 * 数据完全来自既有接口（getAll + getImages），不引入后端 schema 变更。
 */
@Component({
  selector: 'app-photo-wall-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './photo-wall-page.html',
  styleUrl: './photo-wall-page.scss',
})
export class PhotoWallPage implements OnInit, OnDestroy {
  private readonly travel = inject(TravelHistoryService);
  private readonly router = inject(Router);
  private readonly objectUrls = new Set<string>();

  readonly loading = signal(true);
  readonly error = signal('');
  readonly trips = signal<WallTrip[]>([]);
  readonly selectedYear = signal<YearFilter>('all');

  /** 可选年份（倒序） */
  readonly years = computed(() => {
    const set = new Set(this.trips().map((trip) => trip.yearLabel));
    return [...set].sort((a, b) => Number(b) - Number(a));
  });

  readonly visibleTrips = computed(() => {
    const year = this.selectedYear();
    return year === 'all'
      ? this.trips()
      : this.trips().filter((trip) => trip.yearLabel === year);
  });

  readonly photoCount = computed(() =>
    this.trips().reduce((sum, trip) => sum + trip.images.length, 0),
  );

  readonly visiblePhotoCount = computed(() =>
    this.visibleTrips().reduce((sum, trip) => sum + trip.images.length, 0),
  );

  async ngOnInit(): Promise<void> {
    try {
      const records = await this.travel.getAll();
      const trips: WallTrip[] = [];
      for (const record of records) {
        const images = await this.travel.getImages(record.id);
        if (images.length === 0) continue;
        const wallImages: WallImage[] = [];
        for (const image of images) {
          wallImages.push({ image, src: await this.blobUrl(image.thumbnailUrl) });
        }
        trips.push({
          id: record.id,
          locationName: record.locationName,
          arrivedAt: record.arrivedAt,
          departedAt: record.departedAt,
          yearLabel: String(new Date(record.arrivedAt).getFullYear()),
          timeLabel: fmtDateTime(record.arrivedAt),
          endLabel: record.departedAt ? fmtDateTime(record.departedAt) : null,
          images: wallImages,
        });
      }
      trips.sort((a, b) => b.arrivedAt.localeCompare(a.arrivedAt));
      this.trips.set(trips);
    } catch {
      this.error.set('照片墙加载失败，请稍后重试。');
    } finally {
      this.loading.set(false);
    }
  }

  ngOnDestroy(): void {
    for (const url of this.objectUrls) URL.revokeObjectURL(url);
  }

  /** 媒体接口需登录态：用带 token 的 HttpClient 取缩略图 blob → 对象 URL（与详情页一致）。 */
  private async blobUrl(path: string): Promise<string> {
    const blob = await this.travel.getMediaBlob(path);
    const url = URL.createObjectURL(blob);
    this.objectUrls.add(url);
    return url;
  }

  protected selectYear(year: YearFilter): void {
    this.selectedYear.set(year);
  }

  protected openTrip(id: number): void {
    void this.router.navigate(['/travels', id]);
  }
}
