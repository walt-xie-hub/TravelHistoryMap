import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ImageLightbox } from '../../components/image-lightbox/image-lightbox';
import type { LightboxItem } from '../../components/image-lightbox/image-lightbox';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelImage } from '../../models/travel-record.model';
import { fmtDateTime } from '../../utils/travel-display';
import { createMediaUrlHandle } from '../../utils/media-url.util';

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

/** 当前灯箱状态：某段旅行 + 起始索引 */
interface WallViewer {
  tripId: number;
  items: LightboxItem[];
  index: number;
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
  imports: [ImageLightbox, RouterLink],
  templateUrl: './photo-wall-page.html',
  styleUrl: './photo-wall-page.scss',
})
export class PhotoWallPage implements OnInit, OnDestroy {
  private readonly travel = inject(TravelHistoryService);
  private readonly router = inject(Router);
  // 媒体 URL 句柄（architecture #1）：统一“鉴权 blob → 对象 URL → dispose 回收”记账
  private readonly media = createMediaUrlHandle({
    fetchBlob: (path) => this.travel.getMediaBlob(path),
  });

  readonly loading = signal(true);
  readonly error = signal('');
  readonly trips = signal<WallTrip[]>([]);
  readonly selectedYear = signal<YearFilter>('all');
  readonly viewer = signal<WallViewer | null>(null);

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
    this.media.dispose();
  }

  /** 媒体接口需登录态：用带 token 的 HttpClient 取 blob → 对象 URL（缓存复用，与详情页一致）。 */
  private async blobUrl(path: string): Promise<string> {
    return this.media.urlFor(path);
  }

  protected selectYear(year: YearFilter): void {
    this.selectedYear.set(year);
  }

  protected openTrip(id: number): void {
    void this.router.navigate(['/travels', id]);
  }

  /** 打开该旅行的全屏灯箱（懒加载原图，滑动/键盘切换）。 */
  protected openViewer(trip: WallTrip, imageIndex: number): void {
    this.viewer.set({
      tripId: trip.id,
      index: imageIndex,
      items: trip.images.map((item) => ({
        key: item.image.id,
        title: item.image.originalFileName,
        load: () => this.loadOriginal(item.image),
      })),
    });
  }

  protected closeViewer(): void {
    this.viewer.set(null);
  }

  /** 从灯箱跳到当前旅行的详情（箭头属性保持 this 绑定，供子组件以纯回调方式调用）。 */
  protected readonly goToViewerTrip = (): void => {
    const viewer = this.viewer();
    if (viewer) this.openTrip(viewer.tripId);
  };

  /** 原图同样需登录态：blob → 对象 URL（进入灯箱切换时才按需拉取，路径缓存复用）。 */
  private async loadOriginal(image: TravelImage): Promise<string> {
    return this.media.urlFor(image.originalUrl);
  }
}
