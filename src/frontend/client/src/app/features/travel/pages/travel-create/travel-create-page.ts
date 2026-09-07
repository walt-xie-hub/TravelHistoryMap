import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import type { AmapClickEvent, AmapLngLat, AmapMap, AmapMarker, AmapPlaceResult, AmapNamespace } from '../../../../../types/amap';
import { AmapLoaderService } from '../../data-access/amap-loader.service';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import { HttpErrorResponse } from '@angular/common/http';

@Component({
  selector: 'app-travel-create-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  templateUrl: './travel-create-page.html',
  styleUrl: './travel-create-page.scss',
})
export class TravelCreatePage implements AfterViewInit, OnDestroy {
  private readonly amapLoader = inject(AmapLoaderService);
  private readonly travel = inject(TravelHistoryService);
  private readonly router = inject(Router);
  private readonly mapContainer = viewChild.required<ElementRef<HTMLDivElement>>('mapContainer');
  private amap: AmapNamespace | null = null;
  private map: AmapMap | null = null;
  private marker: AmapMarker | null = null;

  protected readonly keyword = signal('');
  protected readonly results = signal<AmapPlaceResult[]>([]);
  protected readonly selected = signal<AmapPlaceResult | null>(null);
  protected readonly arrivedAt = signal('');
  protected readonly departedAt = signal('');
  protected readonly description = signal('');
  protected readonly files = signal<File[]>([]);
  protected readonly loading = signal(false);
  protected readonly searching = signal(false);
  protected readonly error = signal('');
  protected readonly selectedCoordinates = computed(() => {
    const coordinates = this.coordinates(this.selected()?.location);
    return coordinates ? `${coordinates[1]}, ${coordinates[0]}` : '';
  });

  ngAfterViewInit(): void {
    void this.initializeMap();
  }

  ngOnDestroy(): void {
    this.map?.destroy();
    this.map = null;
    this.marker = null;
  }

  private async initializeMap(): Promise<void> {
    try {
      await this.amapLoader.load();
      this.amap = window.AMap ?? null;
      if (!this.amap) throw new Error('高德地图 SDK 未就绪。');
      this.map = new this.amap.Map(this.mapContainer().nativeElement, {
        viewMode: '2D',
        zoom: 5,
        center: [104.1954, 35.8617],
      });
      this.map.on('click', (event) => this.chooseMapPoint(event));
      const selected = this.selected();
      const selectedCoordinates = this.coordinates(selected?.location);
      if (selected && selectedCoordinates) {
        this.showMarker(selectedCoordinates, selected.name ?? '已选位置');
      }
    } catch (err) {
      this.error.set(err instanceof Error ? err.message : '地图加载失败。');
    }
  }

  protected async search(): Promise<void> {
    const keyword = this.keyword().trim();
    if (!keyword) return;
    this.searching.set(true);
    this.error.set('');
    try {
      await this.amapLoader.load();
      const amap = window.AMap;
      if (!amap?.plugin) throw new Error('地点搜索服务不可用，请检查高德地图配置。');
      await new Promise<void>((resolve) => {
        amap.plugin!(['AMap.PlaceSearch'], () => {
          if (!amap.PlaceSearch) {
            this.error.set('地点搜索插件加载失败，请刷新页面后重试。');
            resolve();
            return;
          }
          const searcher = new amap.PlaceSearch!({ pageSize: 10 });
          searcher.search(keyword, (_status, result) => {
            this.results.set(result.poiList?.pois ?? []);
            resolve();
          });
        });
      });
    } catch (err) {
      this.error.set(err instanceof Error ? err.message : '地点搜索失败。');
    } finally {
      this.searching.set(false);
    }
  }

  protected choose(place: AmapPlaceResult): void {
    this.selected.set(place);
    this.results.set([]);
    const coordinates = this.coordinates(place.location);
    if (coordinates) this.showMarker(coordinates, place.name ?? this.keyword());
  }

  private chooseMapPoint(event: AmapClickEvent): void {
    const coordinates = this.coordinates(event.lnglat);
    if (!coordinates) return;
    const place: AmapPlaceResult = {
      name: '地图选定位置',
      location: coordinates,
    };
    this.selected.set(place);
    this.showMarker(coordinates, place.name!);
    this.error.set('');
    void this.reverseGeocode(coordinates);
  }

  private async reverseGeocode(position: AmapLngLat): Promise<void> {
    const amap = this.amap;
    if (!amap?.plugin) return;
    await new Promise<void>((resolve) => {
      amap.plugin!(['AMap.Geocoder'], () => {
        if (!amap.Geocoder) {
          resolve();
          return;
        }
        const geocoder = new amap.Geocoder({ radius: 1000 });
        geocoder.getAddress(position, (status, result) => {
          if (status === 'complete') {
            const address = result.regeocode?.formattedAddress;
            if (address) {
              this.selected.update(current => current ? { ...current, name: address, address } : current);
              this.showMarker(position, address);
            }
          }
          resolve();
        });
      });
    });
  }

  private showMarker(position: AmapLngLat, title: string): void {
    if (!this.map || !this.amap) return;
    if (this.marker) this.map.remove(this.marker);
    this.marker = new this.amap.Marker({
      position,
      title,
    });
    this.map.add(this.marker);
    this.map.setZoomAndCenter(14, position);
  }

  protected onFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    const selectedFiles = Array.from(input.files ?? []);
    if (selectedFiles.length > 9) {
      this.error.set('最多上传 9 张图片。');
      this.files.set(selectedFiles.slice(0, 9));
      return;
    }
    if (selectedFiles.some((file) => file.size > 10 * 1024 * 1024 || !['image/jpeg', 'image/png', 'image/webp'].includes(file.type))) {
      this.error.set('图片仅支持 JPEG、PNG、WebP，且单张不超过 10 MB。');
      return;
    }
    this.files.set(selectedFiles);
  }

  protected async submit(): Promise<void> {
    const place = this.selected();
    const coordinates = this.coordinates(place?.location);
    if (!place || !coordinates || !this.arrivedAt()) {
      this.error.set('请选择地点并填写到达时间。');
      return;
    }
    const arrived = new Date(this.arrivedAt());
    const departed = this.departedAt() ? new Date(this.departedAt()) : null;
    if (Number.isNaN(arrived.getTime()) || (departed && Number.isNaN(departed.getTime())) || (departed && departed < arrived)) {
      this.error.set('请检查出游时间，离开时间不能早于到达时间。');
      return;
    }
    this.loading.set(true);
    this.error.set('');
    try {
      const record = await this.travel.create({
        locationName: place.name ?? this.keyword().trim(),
        longitude: coordinates[0],
        latitude: coordinates[1],
        arrivedAt: arrived.toISOString(),
        departedAt: departed?.toISOString() ?? null,
        description: this.description().trim() || null,
      });
      for (const file of this.files()) await this.travel.uploadImage(record.id, file);
      await this.router.navigate(['/travels', record.id]);
    } catch (error) {
      this.error.set(
        error instanceof HttpErrorResponse
          ? error.error?.error ?? error.error?.message ?? `保存失败（HTTP ${error.status}）。`
          : '保存失败，请检查网络和图片格式后重试。',
      );
    } finally {
      this.loading.set(false);
    }
  }

  private coordinates(location: AmapPlaceResult['location']): AmapLngLat | null {
    if (!location) return null;
    return Array.isArray(location) ? location : [location.getLng(), location.getLat()];
  }
}