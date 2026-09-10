import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
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
import type { TravelCreatePrefill } from '../../models/travel-record.model';
import { toDatetimeLocal } from '../../utils/travel-display';
import { HttpErrorResponse } from '@angular/common/http';
import { RichTextEditorComponent } from '../../components/rich-text-editor/rich-text-editor';
import { TravelIconPicker } from '../../components/travel-icon-picker/travel-icon-picker';
import { visibleTextLength } from '../../utils/rich-text.util';
import { acceptImageFiles, pastedImageFiles } from '../../utils/staged-images.util';

/** 已选、待上传的本地图片：File 本体 + 预览用本地对象 URL。
 *  尚未成为该记录的 Travel image——保存（create 成功并逐张上传）后才持久化。 */
interface SelectedImage {
  file: File;
  url: string;
}

/** 去掉行政后缀（上海市→上海、中山市→中山、地区/盟），让时间轴节点短。 */
function stripAdminSuffix(name: string): string {
  return name.replace(/(自治州|地区|盟)$/, '').replace(/市$/, '');
}

function firstString(value: unknown): string {
  return typeof value === 'string' && value.trim() ? value.trim() : '';
}

/** 从 AMap 搜索候选提取城市（ADR-0015）：cityname ?? pname。 */
function cityFromPlace(place: AmapPlaceResult): string {
  const extended = place as unknown as { cityname?: unknown; pname?: unknown };
  const raw = firstString(extended.cityname) || firstString(extended.pname);
  return raw ? stripAdminSuffix(raw) : '';
}

/** 从逆地理 addressComponent 提取城市：city ?? province。 */
function cityFromComponents(comp?: { province?: string; city?: string | string[] }): string {
  const cityValue = comp?.city;
  const city = typeof cityValue === 'string' ? cityValue : Array.isArray(cityValue) ? cityValue[0] ?? '' : '';
  const raw = firstString(city) || firstString(comp?.province);
  return raw ? stripAdminSuffix(raw) : '';
}

@Component({
  selector: 'app-travel-create-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, RichTextEditorComponent, TravelIconPicker],
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
  protected readonly city = signal('');
  protected readonly arrivedAt = signal('');
  protected readonly departedAt = signal('');
  protected readonly description = signal('');
  protected readonly descriptionCount = computed(() => visibleTextLength(this.description()));
  // ADR-0014：标签 + 精选收藏
  protected readonly tags = signal<string[]>([]);
  protected readonly tagInput = signal('');
  protected readonly favorite = signal(false);
  // ADR-0016：旅行标识图标（null = 按城市自动）
  protected readonly iconKey = signal<string | null>(null);
  protected readonly files = signal<SelectedImage[]>([]);
  protected readonly loading = signal(false);
  protected readonly searching = signal(false);
  protected readonly error = signal('');
  protected readonly selectedCoordinates = computed(() => {
    const coordinates = this.coordinates(this.selected()?.location);
    return coordinates ? `${coordinates[1]}, ${coordinates[0]}` : '';
  });

  constructor() {
    // “再来一次”预填：由触发方（侧栏/详情等）经 router state 传入地点/坐标/城市
    const state = this.router.getCurrentNavigation()?.extras.state as
      | { prefill?: TravelCreatePrefill }
      | undefined;
    const prefill = state?.prefill;
    if (prefill) {
      this.selected.set({
        name: prefill.locationName,
        location: [prefill.longitude, prefill.latitude],
      } as unknown as AmapPlaceResult);
      this.city.set(prefill.city?.trim() ? prefill.city.trim() : '');
      // 旅行标识随预填带入（显式选择优先于城市派生，ADR-0016）
      this.iconKey.set(prefill.iconKey ?? null);
      // 缺省到达时间=现在；离开留空（进行中）
      this.arrivedAt.set(prefill.arrivedAt ?? toDatetimeLocal(new Date()));
    }
  }

  ngAfterViewInit(): void {
    void this.initializeMap();
  }

  ngOnDestroy(): void {
    this.map?.destroy();
    this.map = null;
    this.marker = null;
    for (const item of this.files()) URL.revokeObjectURL(item.url);
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
    this.city.set(cityFromPlace(place));
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
            const city = cityFromComponents(result.regeocode?.addressComponent);
            if (city) this.city.set(city);
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
    const picked = Array.from(input.files ?? []);
    input.value = ''; // 允许再次选择同一文件 / 相机重复拍摄
    this.stageFiles(picked);
  }

  /** 图片暂存统一入口：文件选择、相机（capture）、剪贴板粘贴都汇聚到这里。 */
  private stageFiles(files: readonly File[]): void {
    const { accepted, error } = acceptImageFiles(files, this.files().length);
    if (accepted.length === 0) {
      this.error.set(error ?? '');
      return;
    }
    // 逐张生成预览用对象 URL（提交成功上传后才成为该记录的 Travel image）
    const additions = accepted.map((file) => ({ file, url: URL.createObjectURL(file) }));
    this.files.update((current) => [...current, ...additions]);
    this.error.set(error ?? ''); // 有裁剪/上限警告则保留提示
  }

  /** 剪贴板粘贴图片（M2 快捷记录）：全局监听，仅截获“带图片”的粘贴。 */
  @HostListener('document:paste', ['$event'])
  protected onDocumentPaste(event: ClipboardEvent): void {
    if (this.loading()) return;
    const pasted = pastedImageFiles(event);
    if (pasted.length === 0) return; // 纯文本粘贴不拦截
    event.preventDefault();
    this.stageFiles(pasted);
  }

  protected removeImage(index: number): void {
    const url = this.files()[index]?.url;
    this.files.update((current) => current.filter((_, i) => i !== index));
    if (url) URL.revokeObjectURL(url);
    this.error.set('');
  }

  // —— ADR-0014 标签 ——

  protected addTag(): void {
    const tag = this.tagInput().trim();
    this.tagInput.set('');
    if (!tag) return;
    const current = this.tags();
    if (current.length >= 8) {
      this.error.set('每条记录最多 8 个标签。');
      return;
    }
    if (tag.length > 20) {
      this.error.set('单个标签不能超过 20 个字符。');
      return;
    }
    if (current.some((item) => item.toLocaleLowerCase() === tag.toLocaleLowerCase())) return;
    this.tags.update((items) => [...items, tag]);
    this.error.set('');
  }

  protected removeTag(index: number): void {
    this.tags.update((items) => items.filter((_, i) => i !== index));
  }


  protected clearImages(): void {
    for (const item of this.files()) URL.revokeObjectURL(item.url);
    this.files.set([]);
    this.error.set('');
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
    if (this.descriptionCount() > 4000) {
      this.error.set('出游描述最多 4000 字。');
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
        description: this.descriptionCount() > 0 ? this.description() : null,
        tags: this.tags(),
        isFavorite: this.favorite(),
        city: this.city() || null,
        iconKey: this.iconKey(),
      });
      // 逐张上传并即时从预览清单移除，成功后释放对象 URL；中途失败则剩余项留在清单便于重试
      for (const item of [...this.files()]) {
        await this.travel.uploadImage(record.id, item.file);
        this.files.update((current) => current.filter((x) => x !== item));
        URL.revokeObjectURL(item.url);
      }
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