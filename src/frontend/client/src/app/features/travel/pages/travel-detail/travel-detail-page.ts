import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelImage, TravelRecord } from '../../models/travel-record.model';
import { RichTextEditorComponent } from '../../components/rich-text-editor/rich-text-editor';
import { isRichHtml, sanitizeRichTextToTrusted, visibleTextLength } from '../../utils/rich-text.util';
import { DomSanitizer } from '@angular/platform-browser';
import type { SafeHtml } from '@angular/platform-browser';

interface DisplayImage extends TravelImage {
  thumbnailSrc: string;
  originalSrc?: string;
}

const MAX_IMAGES = 9;

@Component({
  selector: 'app-travel-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, RichTextEditorComponent],
  templateUrl: './travel-detail-page.html',
  styleUrl: './travel-detail-page.scss',
})
export class TravelDetailPage implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly travel = inject(TravelHistoryService);
  private readonly sanitizer = inject<DomSanitizer>(DomSanitizer);
  private readonly objectUrls = new Set<string>();

  protected readonly record = signal<TravelRecord | null>(null);
  protected readonly images = signal<DisplayImage[]>([]);
  protected readonly selectedImage = signal<DisplayImage | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  // —— 编辑态：仅“正文 + 图片增删”可编辑；地点快照与到达/离开边界保持只读（见 ADR-0008/0009）——
  protected readonly editing = signal(false);
  protected readonly editDescription = signal('');
  protected readonly descriptionCount = computed(() => visibleTextLength(this.editDescription()));
  /** 已选但尚未上传（点“保存”才逐张上传）的图片 */
  protected readonly pendingFiles = signal<File[]>([]);
  protected readonly saving = signal(false);
  protected readonly remainingSlots = computed(() => MAX_IMAGES - this.images().length - this.pendingFiles().length);

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

  // —— 编辑开关 ——

  protected startEdit(): void {
    const item = this.record();
    if (!item || this.saving()) return;
    this.editDescription.set(item.description ?? '');
    this.pendingFiles.set([]);
    this.error.set('');
    this.editing.set(true);
  }

  protected cancelEdit(): void {
    if (this.saving()) return;
    this.editing.set(false);
    this.pendingFiles.set([]);
    this.editDescription.set('');
    this.error.set('');
  }

  protected hasUnsavedChanges(): boolean {
    if (!this.editing() || !this.record()) return false;
    if (this.pendingFiles().length > 0) return true;
    const item = this.record()!;
    return this.normalizedDescription() !== (item.description ?? null);
  }

  /** 是否为可安全按 HTML 渲染的正文（旧版纯文本按纯文本显示，见 ADR-0009）。 */
  protected isRichDescription(text: string | null | undefined): boolean {
    return isRichHtml(text);
  }

  /** 只读渲染：DOMPurify 按能力画像消毒后标记可信，保留安全的行内 style（颜色/字体/字号/对齐）。 */
  protected safeHtml(html: string): SafeHtml {
    return sanitizeRichTextToTrusted(this.sanitizer, html);
  }

  /** 归一化后的入库正文：无可视内容返回 null（富文本空段等同无描述）。 */
  private normalizedDescription(): string | null {
    const html = this.editDescription();
    return visibleTextLength(html) > 0 ? html : null;
  }

  protected leaveWhileEditing(): void {
    if (this.hasUnsavedChanges() && !window.confirm('有未保存的修改，确定离开吗？')) return;
    void this.router.navigate(['/map']);
  }

  // —— 图片：新增（暂存）——

  protected onAddFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    const allSelected = Array.from(input.files ?? []);
    input.value = ''; // 允许再次选择同一文件
    const capacity = this.remainingSlots();
    if (capacity <= 0) {
      this.error.set('每条旅游记录最多 9 张图片，已达上限。');
      return;
    }
    let selected = allSelected;
    if (selected.length > capacity) {
      this.error.set(`最多还能添加 ${capacity} 张图片。`);
      selected = selected.slice(0, capacity);
    }
    if (selected.some((file) => file.size > 10 * 1024 * 1024 || !['image/jpeg', 'image/png', 'image/webp'].includes(file.type))) {
      this.error.set('图片仅支持 JPEG、PNG、WebP，且单张不超过 10 MB。');
      return;
    }
    this.pendingFiles.update(files => [...files, ...selected]);
    this.error.set('');
  }

  protected removeStaged(index: number): void {
    this.pendingFiles.update(files => files.filter((_, i) => i !== index));
  }

  // —— 图片：删除（确认后立即提交，不可恢复）——

  protected async removeImage(image: DisplayImage): Promise<void> {
    const item = this.record();
    if (!item || this.saving()) return;
    if (!window.confirm(`确定删除图片“${image.originalFileName}”吗？删除后不可恢复。`)) return;
    this.error.set('');
    try {
      await this.travel.deleteImage(item.id, image.id);
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 404) {
        // 图片已不存在（如被并发删除）：本地照常移除
        this.removeDisplayImage(image);
        return;
      }
      this.error.set(this.message(err, '删除图片失败，请稍后重试。'));
      return;
    }
    this.removeDisplayImage(image);
  }

  // —— 保存：先 PUT 文字描述，再逐张上传新增图片 ——

  protected async save(): Promise<void> {
    const item = this.record();
    if (!item || this.saving()) return;
    const description = this.normalizedDescription();
    if (this.descriptionCount() > 4000) {
      this.error.set('文字描述最多 4000 字。');
      return;
    }
    if (description === (item.description ?? null) && this.pendingFiles().length === 0) {
      this.cancelEdit();
      return;
    }

    this.saving.set(true);
    this.error.set('');
    try {
      // 1) 正文：PUT 为全量替换，地点快照与时间沿用已加载的只读值（富文本入库由后端消毒/校验）
      const updated = await this.travel.update(item.id, {
        locationName: item.locationName,
        latitude: item.latitude,
        longitude: item.longitude,
        arrivedAt: item.arrivedAt,
        departedAt: item.departedAt,
        description,
      });
      this.record.set(updated);
      this.editDescription.set(updated.description ?? '');

      // 2) 新增图片：逐张上传；成功的即时入列，失败的保留在待上传清单以便重试
      const failed: string[] = [];
      for (const file of [...this.pendingFiles()]) {
        let uploaded: TravelImage;
        try {
          uploaded = await this.travel.uploadImage(item.id, file);
        } catch {
          failed.push(file.name);
          continue;
        }
        this.pendingFiles.update(files => files.filter(f => f !== file));
        let thumbnailSrc = '';
        try {
          thumbnailSrc = await this.toObjectUrl(uploaded.thumbnailUrl);
        } catch {
          // 缩略图加载失败不影响已上传结果；刷新后会恢复
        }
        this.images.update(items => [...items, { ...uploaded, thumbnailSrc }]);
      }

      if (failed.length === 0) {
        this.editing.set(false);
        this.pendingFiles.set([]);
        this.editDescription.set('');
      } else {
        this.error.set(`有 ${failed.length} 张图片上传失败：${failed.join('、')}。可重试或移除后重新保存。`);
      }
    } catch (err) {
      this.error.set(this.message(err, '保存失败，请稍后重试。'));
    } finally {
      this.saving.set(false);
    }
  }

  // —— 原图灯箱 ——

  protected async openOriginal(image: DisplayImage): Promise<void> {
    if (this.saving()) return;
    if (!image.originalSrc) {
      image.originalSrc = await this.toObjectUrl(image.originalUrl);
      this.images.update(items => [...items]);
    }
    this.selectedImage.set(image);
  }

  protected closeOriginal(): void {
    this.selectedImage.set(null);
  }

  // —— 工具 ——

  private removeDisplayImage(image: DisplayImage): void {
    this.images.update(items => items.filter(x => x !== image));
    if (this.selectedImage() === image) this.selectedImage.set(null);
    this.revoke(image.thumbnailSrc);
    this.revoke(image.originalSrc);
  }

  private revoke(url?: string): void {
    if (url && this.objectUrls.delete(url)) URL.revokeObjectURL(url);
  }

  private async toObjectUrl(path: string): Promise<string> {
    const blob = await this.travel.getMediaBlob(path);
    const url = URL.createObjectURL(blob);
    this.objectUrls.add(url);
    return url;
  }

  private message(err: unknown, fallback: string): string {
    return err instanceof HttpErrorResponse
      ? err.error?.error ?? err.error?.message ?? `操作失败（HTTP ${err.status}）。`
      : fallback;
  }
}
