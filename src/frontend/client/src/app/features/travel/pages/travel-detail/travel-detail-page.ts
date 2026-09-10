import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, HostListener, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TravelHistoryService } from '../../data-access/travel-history.service';
import type { TravelImage, TravelRecord } from '../../models/travel-record.model';
import { toDatetimeLocal } from '../../utils/travel-display';
import { RichTextEditorComponent } from '../../components/rich-text-editor/rich-text-editor';
import { TravelShareDialog } from '../../components/travel-share-dialog/travel-share-dialog';
import { isRichHtml, sanitizeRichTextToTrusted, visibleTextLength } from '../../utils/rich-text.util';
import { acceptImageFiles, pastedImageFiles } from '../../utils/staged-images.util';
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
  imports: [DatePipe, RouterLink, RichTextEditorComponent, TravelShareDialog],
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
  protected readonly favoriteBusy = signal(false);

  // —— 编辑态：正文 + 图片增删可编辑；进行中记录可“补记离开时间”（地点/到达仍只读，ADR-0008/09）——
  protected readonly editing = signal(false);
  protected readonly shareOpen = signal(false);
  protected readonly editDescription = signal('');
  /** 进行中收尾：补记的离开时间（datetime-local 值；空=保持进行中） */
  protected readonly editDepartAt = signal('');
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
      // “结束停留”深链（侧栏入口 ?finish=1）：进入编辑并预填离开时间=现在
      if (!record.departedAt && this.route.snapshot.queryParamMap.get('finish') === '1') {
        this.startEdit();
        this.editDepartAt.set(toDatetimeLocal(new Date()));
      }
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
    this.editDepartAt.set('');
    this.pendingFiles.set([]);
    this.error.set('');
    this.editing.set(true);
  }

  protected cancelEdit(): void {
    if (this.saving()) return;
    this.editing.set(false);
    this.pendingFiles.set([]);
    this.editDescription.set('');
    this.editDepartAt.set('');
    this.error.set('');
  }

  /** “再来一次”：预填本地点（名称/坐标/城市）跳到新建页，到达=现在、离开留空 */
  protected recordAgain(item: TravelRecord): void {
    void this.router.navigate(['/travels/new'], {
      state: {
        prefill: {
          locationName: item.locationName,
          longitude: item.longitude,
          latitude: item.latitude,
          city: item.city ?? null,
        },
      },
    });
  }

  /** 结束停留：进入编辑并把离开时间预填为现在（可在编辑面板修改后保存） */
  protected finishNow(): void {
    this.startEdit();
    this.editDepartAt.set(toDatetimeLocal(new Date()));
  }

  protected hasUnsavedChanges(): boolean {
    if (!this.editing() || !this.record()) return false;
    if (this.pendingFiles().length > 0) return true;
    const item = this.record()!;
    if (item.departedAt === null && this.editDepartAt() !== '') return true; // 进行中收尾：填了离开时间
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

  // —— ADR-0014 收藏切换（用整条记录全量更新，保留正文/标签/时间不变） ——

  protected async toggleFavorite(item: TravelRecord): Promise<void> {
    if (this.favoriteBusy() || this.saving()) return;
    this.favoriteBusy.set(true);
    this.error.set('');
    try {
      const updated = await this.travel.update(item.id, {
        locationName: item.locationName,
        latitude: item.latitude,
        longitude: item.longitude,
        arrivedAt: item.arrivedAt,
        departedAt: item.departedAt,
        description: item.description,
        tags: item.tags ?? [],
        isFavorite: !item.isFavorite,
        city: item.city ?? null,
      });
      this.record.set(updated);
    } catch (err) {
      this.error.set(this.message(err, '收藏操作失败，请稍后重试。'));
    } finally {
      this.favoriteBusy.set(false);
    }
  }

  // —— 图片：新增（暂存）——

  protected onAddFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    const allSelected = Array.from(input.files ?? []);
    input.value = ''; // 允许再次选择同一文件 / 相机重复拍摄
    this.stagePending(allSelected);
  }

  /** 图片暂存统一入口：文件选择、相机（capture）、剪贴板粘贴都汇聚到这里。 */
  private stagePending(files: readonly File[]): void {
    const { accepted, error } = acceptImageFiles(files, this.images().length + this.pendingFiles().length);
    if (accepted.length === 0) {
      this.error.set(error ?? '');
      return;
    }
    this.pendingFiles.update((current) => [...current, ...accepted]);
    this.error.set(error ?? ''); // 有裁剪/上限警告则保留提示
  }

  /** 剪贴板粘贴图片（M2 快捷记录）：仅编辑态生效；纯文本粘贴不拦截。 */
  @HostListener('document:paste', ['$event'])
  protected onDocumentPaste(event: ClipboardEvent): void {
    if (this.saving() || !this.editing() || !this.record()) return;
    const pasted = pastedImageFiles(event);
    if (pasted.length === 0) return;
    event.preventDefault();
    this.stagePending(pasted);
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

    // 进行中收尾：解析补记的离开时间（默认已在编辑前预填为现在）
    const leaveInput = this.editDepartAt().trim();
    const departChanged = item.departedAt === null && leaveInput !== '';
    let departValue: string | null = item.departedAt;
    if (departChanged) {
      const leave = new Date(leaveInput);
      const arrived = new Date(item.arrivedAt);
      if (Number.isNaN(leave.getTime()) || leave < arrived) {
        this.error.set('离开时间不能早于到达时间。');
        return;
      }
      departValue = leave.toISOString();
    }

    if (description === (item.description ?? null) && !departChanged && this.pendingFiles().length === 0) {
      this.cancelEdit();
      return;
    }

    this.saving.set(true);
    this.error.set('');
    try {
      // 1) PUT 全量替换（富文本入库由后端消毒/校验）；进行中收尾时写回补记的离开时间
      const updated = await this.travel.update(item.id, {
        locationName: item.locationName,
        latitude: item.latitude,
        longitude: item.longitude,
        arrivedAt: item.arrivedAt,
        departedAt: departValue,
        description,
        // ADR-0014：PUT 全量替换，须回传标签/收藏，否则会被清空；城市同（ADR-0015）
        tags: item.tags ?? [],
        isFavorite: item.isFavorite ?? false,
        city: item.city ?? null,
      });
      this.record.set(updated);
      this.editDescription.set(updated.description ?? '');
      this.editDepartAt.set('');

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
