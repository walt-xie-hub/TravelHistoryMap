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
  const collapsed = decodeHtmlEntities(withoutTags).replace(/\s+/g, ' ').trim();
  return collapsed.length;
}

/** 消毒器可能产出的具名实体（浏览器里 DOM 解码的结果与此一致，此处无需 DOM） */
const NAMED_ENTITIES: Readonly<Record<string, string>> = {
  amp: '&',
  lt: '<',
  gt: '>',
  quot: '"',
  apos: "'",
  nbsp: '\u00a0',
  hellip: '…',
  middot: '·',
  ldquo: '“',
  rdquo: '”',
  mdash: '—',
  ndash: '–',
};

/**
 * 解码 HTML 实体（纯函数、不依赖 DOM）：覆盖具名实体（above）与全部数字实体（十进制/十六进制）。
 * 不可识别的实体原样保留。做成纯函数是为了让计数/摘要逻辑能被 node 环境下的单测直接覆盖。
 */
export function decodeHtmlEntities(value: string): string {
  return value.replace(/&(#x?[0-9a-f]+|[a-z]+);/gi, (whole, body: string) => {
    if (!body.startsWith('#')) return NAMED_ENTITIES[body.toLowerCase()] ?? whole;
    const code =
      body[1]?.toLowerCase() === 'x' ? parseInt(body.slice(2), 16) : Number(body.slice(1));
    return Number.isFinite(code) && code > 0 && code <= 0x10ffff
      ? String.fromCodePoint(code)
      : whole;
  });
}

/**
 * 正文 → 单行纯文本：去标签、解码 HTML 实体、折叠空白（旧版纯文本与富文本一视同仁）。
 * 传入 `max` 时超长截断加省略号（摘要场景）。分享卡与明信片清单共用这一个实现。
 */
export function plainTextSnippet(html: string | null | undefined, max?: number): string {
  if (!html) return '';
  const text = decodeHtmlEntities(html.replace(/<[^>]*>/g, ' '))
    .replace(/\s+/g, ' ')
    .trim();
  if (max === undefined || text.length <= max) return text;
  return `${text.slice(0, max)}…`;
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
export function sanitizeRichTextToTrusted(
  sanitizer: DomSanitizer,
  html: string | null | undefined,
): SafeHtml {
  const clean = DOMPurify.sanitize(html ?? '', {
    ALLOWED_TAGS: [...RICH_TEXT_PROFILE.tags],
    ALLOWED_ATTR: [...RICH_TEXT_PROFILE.attrs],
    KEEP_CONTENT: true,
  });
  return sanitizer.bypassSecurityTrustHtml(clean);
}
