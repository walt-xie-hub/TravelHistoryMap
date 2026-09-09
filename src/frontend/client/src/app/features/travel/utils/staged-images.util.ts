/**
 * 图片暂存统一规则（M2 快捷记录）。
 *
 * 创建页 / 详情编辑页“新增图片”共用同一套容量与格式校验，
 * 并把三种来源——文件选择、相机拍照（capture）、剪贴板粘贴（Ctrl/⌘+V）——
 * 归一化成统一的 File 追加逻辑，避免两处各自维护重复校验。
 *
 * 限制与既有上传保持一致：JPEG/PNG/WebP，单张 ≤10MB，每条最多 9 张。
 */

export const MAX_IMAGE_COUNT = 9;
export const MAX_IMAGE_BYTES = 10 * 1024 * 1024;
const ALLOWED_MIME = new Set(['image/jpeg', 'image/png', 'image/webp']);

export interface ImageAcceptResult {
  /** 通过校验并被裁剪到剩余容量后的图片（可能为空）。 */
  accepted: File[];
  /** 未通过 / 被裁剪时的提示文案；完全成功时为 null。 */
  error: string | null;
}

export function isAcceptedImageFile(file: File): boolean {
  return (
    file.size > 0 &&
    file.size <= MAX_IMAGE_BYTES &&
    ALLOWED_MIME.has(file.type)
  );
}

/** 统一校验 + 容量裁剪：任何来源进入暂存队列前都经过这里。 */
export function acceptImageFiles(
  files: readonly File[],
  currentCount: number,
): ImageAcceptResult {
  const capacity = MAX_IMAGE_COUNT - currentCount;
  if (capacity <= 0) {
    return { accepted: [], error: `每条旅游记录最多 ${MAX_IMAGE_COUNT} 张图片，已达上限。` };
  }
  if (files.length === 0) return { accepted: [], error: null };
  if (files.some((file) => !isAcceptedImageFile(file))) {
    return {
      accepted: [],
      error: `图片仅支持 JPEG、PNG、WebP，且单张不超过 ${Math.round(MAX_IMAGE_BYTES / 1024 / 1024)} MB。`,
    };
  }
  if (files.length > capacity) {
    return { accepted: files.slice(0, capacity), error: `最多还能添加 ${capacity} 张图片。` };
  }
  return { accepted: files.slice(), error: null };
}

/** 从粘贴事件中提取剪贴板内的图片文件（纯文本粘贴返回空数组，不拦截）。 */
export function pastedImageFiles(event: ClipboardEvent): File[] {
  const data = event.clipboardData;
  const files: File[] = [];
  const seen = new Set<File>();
  const push = (file: File | null): void => {
    if (!file || !file.type.startsWith('image/') || seen.has(file)) return;
    seen.add(file);
    files.push(normalizeClipboardFile(file));
  };

  if (data?.items) {
    for (let index = 0; index < data.items.length; index += 1) {
      const item = data.items[index];
      if (item.kind === 'file') push(item.getAsFile());
    }
  }
  if (data?.files) {
    for (const file of Array.from(data.files)) push(file);
  }
  return files;
}

/** 剪贴板截图常以泛名/空名出现，给它一个可读且带正确扩展名的名字。 */
function normalizeClipboardFile(file: File): File {
  const looksGeneric = /^image(\.\w+)?$/i.test(file.name) || !file.name.includes('.');
  const name = looksGeneric
    ? `pasted-${timestampName()}.${extensionFor(file.type)}`
    : file.name;
  return file.name === name
    ? file
    : new File([file], name, { type: file.type, lastModified: Date.now() });
}

function extensionFor(mime: string): string {
  switch (mime) {
    case 'image/jpeg':
      return 'jpg';
    case 'image/webp':
      return 'webp';
    case 'image/gif':
      return 'gif';
    default:
      return 'png';
  }
}

function timestampName(): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  const now = new Date();
  const date = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}`;
  const time = `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
  return `${date}-${time}-${Math.floor(Math.random() * 1000)}`;
}
