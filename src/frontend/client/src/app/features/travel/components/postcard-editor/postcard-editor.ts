import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelImage, TravelRecord } from '../../models/travel-record.model';
import { PostcardCanvas } from '../postcard-canvas/postcard-canvas';
import {
  POSTCARD_ADDRESS_MAX,
  POSTCARD_DATE_MAX,
  POSTCARD_MESSAGE_MAX,
  POSTCARD_PLACE_MAX,
  POSTCARD_POSTAL_CODE_MAX,
  POSTCARD_RECIPIENT_MAX,
  POSTCARD_SIGNATURE_MAX,
  POSTCARD_TITLE_MAX,
  changePostcardTemplate,
  createPostcardContent,
  postcardFileName,
  setMainRecord,
  togglePostcardModule,
  toggleTrailRecord,
  type PostcardContent,
  type PostcardFields,
} from '../../postcard/postcard-model';
import { downloadBlob, exportPostcardBlob, fileToDataUrl } from '../../postcard/postcard-export';
import {
  POSTCARD_TEMPLATES,
  POSTCARD_MODULE_LABELS,
  postcardTemplateById,
  switchableModules,
  type PostcardModuleId,
} from '../../postcard/postcard-template';
import {
  STAMP_DENOMINATIONS,
  defaultStampFace,
  stampCaption,
  validateStampImage,
  withStampDenomination,
  withStampYear,
} from '../../postcard/postcard-stamp';

/**
 * 明信片编辑器（ADR-0018）：左选模板、中看实时预览、右改内容与模块。
 *
 * 预览用的就是导出用的那份 DOM（同一组件、同一尺寸），所以"所见即所得"不是靠对齐两套实现，
 * 而是根本只有一套；预览的缩放挂在父容器上，导出节点始终是 1181×1748。
 */
@Component({
  selector: 'app-postcard-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PostcardCanvas],
  templateUrl: './postcard-editor.html',
  styleUrl: './postcard-editor.scss',
})
export class PostcardEditor {
  readonly records = input<readonly TravelRecord[]>([]);
  readonly close = output<void>();

  private readonly travel = inject(TravelHistoryService);

  /**
   * 导出用的画面（屏幕上的那一份，保证图片已解码完成）。
   * 模板引用挂在组件元素上，因此必须显式 `read: ElementRef` —— 否则拿到的是组件实例。
   */
  private readonly canvasRef = viewChild.required('previewCanvas', {
    read: ElementRef<HTMLElement>,
  });

  protected readonly templates = POSTCARD_TEMPLATES;
  protected readonly moduleLabels = POSTCARD_MODULE_LABELS;
  protected readonly denominations = STAMP_DENOMINATIONS;
  protected readonly limits = {
    title: POSTCARD_TITLE_MAX,
    message: POSTCARD_MESSAGE_MAX,
    signature: POSTCARD_SIGNATURE_MAX,
    recipient: POSTCARD_RECIPIENT_MAX,
    address: POSTCARD_ADDRESS_MAX,
    postalCode: POSTCARD_POSTAL_CODE_MAX,
    place: POSTCARD_PLACE_MAX,
    date: POSTCARD_DATE_MAX,
  };

  protected readonly content = signal<PostcardContent>(createPostcardContent({ records: [] }));
  protected readonly template = computed(() => postcardTemplateById(this.content().templateId));
  /** 面板里真正该出现的开关：单条记录时不给「足迹清单」（它本就无处可列，不摆空操作开关） */
  protected readonly moduleSwitches = computed(() =>
    switchableModules(this.template()).filter((moduleId) => moduleId !== 'trail' || this.isMulti()),
  );
  protected readonly stampLabel = computed(() => stampCaption(this.content().stamp));
  protected readonly isMulti = computed(() => this.records().length > 1);

  /** 主记录的图片（只有主记录的图片可作主图，ADR-0018 决策 5）：选择器用缩略图 */
  protected readonly mainImages = signal<readonly TravelImage[]>([]);
  /** 缩略图地址：选择器小格子用，省流量 */
  protected readonly thumbUrls = signal<ReadonlyMap<number, string>>(new Map());
  /** 原图地址：**画布置用原图**——缩略图（320px）撑 1181px 的明信片会明显发虚 */
  protected readonly imageUrls = signal<ReadonlyMap<number, string>>(new Map());
  protected readonly imagesLoading = signal(false);
  protected readonly missingImages = computed(() => {
    const template = this.template();
    return Math.max(0, template.imageCount - this.content().imageIds.length);
  });

  protected readonly exporting = signal(false);
  protected readonly error = signal('');

  /** 图片请求的过期令牌：快速切主角/连点选择时，晚到的结果直接丢弃 */
  private imagesSeq = 0;

  constructor() {
    const destroyRef = inject(DestroyRef);
    /**
     * 记录集变化时重建内容（弹层每次打开都是新的一次明信片）。
     * 图片加载在这里**显式发起**，而不是让另一个 effect 去读 `content()`——
     * 否则每次改一个字都会重建内容信号、连带重拉图片列表。
     */
    effect(() => {
      const records = this.records();
      if (!records.length) {
        this.content.set(createPostcardContent({ records: [] }));
        return;
      }
      const content = createPostcardContent({ records });
      this.content.set(content);
      void this.loadImages(content.mainRecordId);
    });
    destroyRef.onDestroy(() => this.releaseImageUrls());
  }

  // ---------- 模板与模块 ----------

  protected selectTemplate(templateId: string): void {
    this.content.update((content) => changePostcardTemplate(content, templateId));
    void this.ensureOriginalUrls();
  }

  protected toggleModule(moduleId: PostcardModuleId, event: Event): void {
    const enabled = (event.target as HTMLInputElement).checked;
    this.content.update((content) => togglePostcardModule(content, moduleId, enabled));
  }

  // ---------- 记录与清单 ----------

  protected selectMainRecord(event: Event): void {
    const recordId = Number((event.target as HTMLSelectElement).value);
    if (!recordId) return;
    // 换主角时主图也重置为该记录的图片，避免留下上一个记录的照片
    this.content.update((content) => {
      const next = setMainRecord(content, this.records(), recordId);
      return { ...next, imageIds: [] };
    });
    void this.loadImages(recordId);
  }

  protected toggleTrail(recordId: number): void {
    this.content.update((content) => toggleTrailRecord(content, recordId));
  }

  protected isInTrail(recordId: number): boolean {
    return this.content().includedRecordIds.includes(recordId);
  }

  // ---------- 图片 ----------

  protected toggleImage(imageId: number): void {
    this.content.update((content) => {
      const template = postcardTemplateById(content.templateId);
      if (template.imageCount === 0) return content;
      const selected = content.imageIds.includes(imageId)
        ? content.imageIds.filter((id) => id !== imageId)
        : [...content.imageIds, imageId].slice(-template.imageCount);
      return { ...content, imageIds: selected };
    });
    void this.ensureOriginalUrls();
  }

  protected isImageSelected(imageId: number): boolean {
    return this.content().imageIds.includes(imageId);
  }

  // ---------- 字段 ----------

  protected updateField<K extends keyof PostcardFields>(key: K, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement).value;
    this.content.update((content) => ({ ...content, fields: { ...content.fields, [key]: value } }));
  }

  // ---------- 邮票 ----------

  protected changeStampYear(event: Event): void {
    const year = Number((event.target as HTMLInputElement).value);
    if (!Number.isFinite(year) || year < 1900 || year > 2100) return;
    this.content.update((content) => ({ ...content, stamp: withStampYear(content.stamp, year) }));
  }

  protected changeStampDenomination(event: Event): void {
    const denomination = (event.target as HTMLSelectElement).value;
    this.content.update((content) => ({
      ...content,
      stamp: withStampDenomination(content.stamp, denomination),
    }));
  }

  protected async onStampFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    const check = validateStampImage(file);
    if (!check.accepted) {
      this.error.set(check.error ?? '邮票图片不可用。');
      return;
    }
    try {
      const dataUrl = await fileToDataUrl(file);
      this.error.set('');
      this.content.update((content) => ({
        ...content,
        stamp: { ...content.stamp, customImageDataUrl: dataUrl, customImageName: file.name },
      }));
    } catch {
      this.error.set('读取邮票图片失败，请换一张。');
    }
  }

  protected clearStampImage(): void {
    this.content.update((content) => ({
      ...content,
      stamp: { ...content.stamp, customImageDataUrl: null, customImageName: null },
    }));
  }

  /** 把票面恢复到"该年份的自绘生肖票" */
  protected resetStampFace(): void {
    this.content.update((content) => ({
      ...content,
      stamp: defaultStampFace(content.stamp.year, content.stamp.denomination),
    }));
  }

  // ---------- 导出 ----------

  protected async export(kind: 'png' | 'jpeg'): Promise<void> {
    if (this.exporting()) return;
    this.exporting.set(true);
    this.error.set('');
    try {
      const node = this.canvasRef().nativeElement;
      const blob = await exportPostcardBlob(node, kind);
      downloadBlob(blob, postcardFileName(this.content(), kind === 'jpeg' ? 'jpg' : 'png'));
    } catch {
      this.error.set('导出失败，请重试（照片较多时多等一会儿）。');
    } finally {
      this.exporting.set(false);
    }
  }

  // ---------- 图片字节（blob URL 生命周期自管） ----------

  /**
   * 拉主记录的图片：列表 + 缩略图（选择器用）。
   * 每次调用都领一个令牌，晚到的结果直接丢弃，不会把旧记录的图片写进来。
   */
  private async loadImages(recordId: number): Promise<void> {
    if (!recordId) {
      this.mainImages.set([]);
      this.releaseImageUrls();
      return;
    }
    const seq = ++this.imagesSeq;
    this.imagesLoading.set(true);
    try {
      const images = await this.travel.getImages(recordId);
      if (seq !== this.imagesSeq) return;
      this.mainImages.set(images);
      this.setThumbUrls(images, seq);
      this.autoSelectImages(recordId);
      await this.ensureOriginalUrls();
    } catch {
      if (seq === this.imagesSeq) this.mainImages.set([]);
    } finally {
      if (seq === this.imagesSeq) this.imagesLoading.set(false);
    }
  }

  /** 缩略图只用同步创建（blob 已经在手）时才开；这里先取字节再建 URL */
  private async setThumbUrls(images: readonly TravelImage[], seq: number): Promise<void> {
    const next = new Map<number, string>();
    for (const image of images) {
      if (seq !== this.imagesSeq) return;
      try {
        next.set(image.id, URL.createObjectURL(await this.travel.getMediaBlob(image.thumbnailUrl)));
      } catch {
        // 单张失败不影响其它图片
      }
    }
    for (const url of this.thumbUrls().values()) URL.revokeObjectURL(url);
    this.thumbUrls.set(next);
  }

  /** 画布用原图：只为当前选中的几张取，切图时多余的释放掉 */
  private async ensureOriginalUrls(): Promise<void> {
    const wanted = this.content().imageIds;
    const images = this.mainImages();
    const current = this.imageUrls();
    const missing = wanted.filter(
      (id) => !current.has(id) && images.some((image) => image.id === id),
    );
    if (missing.length) {
      const next = new Map(current);
      for (const id of missing) {
        const image = images.find((candidate) => candidate.id === id);
        if (!image) continue;
        try {
          next.set(id, URL.createObjectURL(await this.travel.getMediaBlob(image.originalUrl)));
        } catch {
          // 单张失败不影响其它图片
        }
      }
      for (const [id, url] of next) {
        if (!wanted.includes(id)) URL.revokeObjectURL(url);
      }
      const kept = new Map<number, string>();
      for (const id of wanted) {
        const url = next.get(id);
        if (url) kept.set(id, url);
      }
      this.imageUrls.set(kept);
      return;
    }
    // 取消勾选后释放不再需要的原图
    const stale = [...current.keys()].filter((id) => !wanted.includes(id));
    if (stale.length) {
      const kept = new Map(current);
      for (const id of stale) {
        const url = kept.get(id);
        if (url) URL.revokeObjectURL(url);
        kept.delete(id);
      }
      this.imageUrls.set(kept);
    }
  }

  /** 还没选图时按模板需要自动带上前几张：打开编辑器就该看到一张有照片的明信片 */
  private autoSelectImages(recordId: number): void {
    if (this.content().mainRecordId !== recordId) return;
    if (this.content().imageIds.length > 0) return;
    const template = postcardTemplateById(this.content().templateId);
    if (template.imageCount === 0) return;
    const auto = this.mainImages()
      .slice(0, template.imageCount)
      .map((image) => image.id);
    if (auto.length) this.content.update((content) => ({ ...content, imageIds: auto }));
  }

  private releaseImageUrls(): void {
    for (const url of this.imageUrls().values()) URL.revokeObjectURL(url);
    for (const url of this.thumbUrls().values()) URL.revokeObjectURL(url);
    this.imageUrls.set(new Map());
    this.thumbUrls.set(new Map());
  }
}
