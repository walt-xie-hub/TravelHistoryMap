/**
 * 富文本正文工具（与后端 Travel.Application/Sanitization/RichTextSanitizer 口径一致，见 ADR-0009/0010）。
 * 前端只负责计数提示与预检；最终合法性/消毒由服务端兜底。
 */
import DOMPurify from 'dompurify';
import type { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { RICH_TEXT_PROFILE } from '../rich-text/rich-text-profile';

/** 是否是可安全按 HTML 渲染的正文：TipTap 产出的内容总带 <p>/<h2> 等块级标签。
 *  旧版纯文本 Description（格式升级前）不含这些标签，一律按纯文本转义显示（ADR-0009 存量兼容）。 */
export function isRichHtml(text: string | null | undefined): boolean {
  if (!text) return false;
  return /<(p|h[1-6]|ul|ol|blockquote)([\s>])/i.test(text);
}

/** 可见字数：与服务端一致 —— 标签替换为空格 → HTML 实体解码 → 连续空白折叠为单空格后计数。 */
export function visibleTextLength(html: string | null | undefined): number {
  if (!html) return 0;
  const withoutTags = html.replace(/<[^>]*>/g, ' ');
  // 仅借 DOM 解码 &amp; 等实体（不渲染给用户，也无脚本执行风险）
  const decoded = decodeEntities(withoutTags);
  const collapsed = decoded.replace(/\s+/g, ' ').trim();
  return collapsed.length;
}

function decodeEntities(value: string): string {
  const div = document.createElement('div');
  div.innerHTML = value;
  return div.textContent ?? '';
}

/** 把旧版纯文本正文转成编辑器可安全加载的 HTML（逐段成 <p>，保留段落）。 */
export function plainTextToHtml(text: string | null | undefined): string {
  if (!text) return '';
  return text
    .split(/\r?\n/)
    .map((line) => `<p>${escapeText(line) || '<br>'}</p>`)
    .join('');
}

function escapeText(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

/**
 * 渲染用客户端消毒（ADR-0010）：按能力画像对 HTML 做 tag/attr 白名单消毒，
 * 保留安全的行内 style（颜色/字体/字号/对齐），再标记为可信交给 [innerHTML]。
 * 服务端仍是在写入侧做权威消毒；此处为读取侧的纵深防御（Angular 内建消毒会剥掉 style）。
 */
export function sanitizeRichTextToTrusted(sanitizer: DomSanitizer, html: string | null | undefined): SafeHtml {
  const clean = DOMPurify.sanitize(html ?? '', {
    ALLOWED_TAGS: [...RICH_TEXT_PROFILE.tags],
    ALLOWED_ATTR: [...RICH_TEXT_PROFILE.attrs],
    KEEP_CONTENT: true,
  });
  return sanitizer.bypassSecurityTrustHtml(clean);
}
