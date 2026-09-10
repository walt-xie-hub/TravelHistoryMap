/**
 * 明信片导出（ADR-0018）：把画面 DOM 截成图片，纯本地、不经过服务端。
 *
 * 用 `html-to-image` 只做"DOM → 字节"这一件事：模板是 HTML/CSS，图片是同源 blob URL、
 * 字体是系统字体栈，因此不涉及外链字体/跨域图片这两个常见的坑。
 * 导出节点是**未缩放的画面本身**（预览的缩放挂在父元素上），所以输出恒为 1181×1748。
 */
import { toBlob, toJpeg } from 'html-to-image';
import { POSTCARD_HEIGHT, POSTCARD_WIDTH } from './postcard-template';

export type PostcardExportKind = 'png' | 'jpeg';

const JPEG_QUALITY = 0.92;

/** 导出画面为 Blob（1:1 像素，不缩放） */
export async function exportPostcardBlob(
  node: HTMLElement,
  kind: PostcardExportKind,
): Promise<Blob> {
  const options = { width: POSTCARD_WIDTH, height: POSTCARD_HEIGHT, pixelRatio: 1 };
  const blob = kind === 'jpeg' ? await toJpegBlob(node, options) : await toBlob(node, options);
  if (!blob) throw new Error('导出失败：画面为空');
  return blob;
}

async function toJpegBlob(
  node: HTMLElement,
  options: { width: number; height: number; pixelRatio: number },
): Promise<Blob | null> {
  // html-to-image 的 toJpeg 返回 dataURL；这里统一转成 Blob（下载链路只认 Blob）
  const dataUrl = await toJpeg(node, {
    ...options,
    quality: JPEG_QUALITY,
    backgroundColor: '#ffffff',
  });
  return dataUrlToBlob(dataUrl);
}

function dataUrlToBlob(dataUrl: string): Blob {
  const [header, payload] = dataUrl.split(',');
  const mime = /:(.*?);/.exec(header ?? '')?.[1] ?? 'image/jpeg';
  const binary = atob(payload ?? '');
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return new Blob([bytes], { type: mime });
}

/** 触发浏览器下载（与足迹卡同一套做法） */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  // 交给浏览器读完再释放
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** Blob → dataURL（上传自备票图时把文件读进来） */
export function fileToDataUrl(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(new Error('读取图片失败'));
    reader.readAsDataURL(file);
  });
}
