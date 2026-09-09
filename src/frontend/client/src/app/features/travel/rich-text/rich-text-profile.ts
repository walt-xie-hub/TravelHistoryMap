/**
 * 富文本“能力画像”（单一事实来源，ADR-0010）。
 * 与后端 Travel.Application/Sanitization/RichTextSanitizer 的 allow-list 保持一致：
 * 允许的标签/属性/CSS 属性同时约束编辑器 schema 与存储/渲染消毒。
 * 行内排版（颜色/字体/字号/对齐/高亮）通过 style 中的受控 CSS 属性表达。
 */

export const RICH_TEXT_PROFILE = {
  /** 允许的标签 */
  tags: [
    'p', 'br', 'strong', 'em', 'u', 's', 'span', 'mark',
    'h2', 'h3', 'ul', 'ol', 'li', 'blockquote', 'a',
  ],
  /** 允许的属性 */
  attrs: ['href', 'style'],
  /** style 中允许的 CSS 属性 */
  cssProps: ['color', 'background-color', 'font-family', 'font-size', 'text-align'],
} as const;

/** 字体族下拉（值为写入 style 的 font-family） */
export const FONT_FAMILIES: ReadonlyArray<{ label: string; value: string }> = [
  { label: '默认字体', value: '' },
  { label: '宋体', value: '"SimSun", "宋体", serif' },
  { label: '黑体', value: '"SimHei", "黑体", sans-serif' },
  { label: '楷体', value: '"KaiTi", "楷体", serif' },
  { label: '微软雅黑', value: '"Microsoft YaHei", "微软雅黑", sans-serif' },
  { label: 'Arial', value: 'Arial, Helvetica, sans-serif' },
];

/** 字号下拉（px） */
export const FONT_SIZES: ReadonlyArray<{ label: string; value: string }> = [
  { label: '默认字号', value: '' },
  { label: '12', value: '12px' },
  { label: '14', value: '14px' },
  { label: '16', value: '16px' },
  { label: '18', value: '18px' },
  { label: '20', value: '20px' },
  { label: '24', value: '24px' },
  { label: '28', value: '28px' },
  { label: '32', value: '32px' },
];
