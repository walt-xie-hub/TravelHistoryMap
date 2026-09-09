/**
 * 富文本引擎抽象（ADR-0010）：编辑器与页面解耦的关键 seam。
 * 页面/组件只依赖本接口 + 能力画像；具体引擎（TipTap 等）作为实现注入。
 * 更换编辑器 = 新增一个 RichTextEngine 实现并在组件 providers 里替换，页面零改动。
 */

export type RichTextAction =
  | 'bold' | 'italic' | 'underline' | 'strike'
  | 'paragraph' | 'h2' | 'h3'
  | 'bulletList' | 'orderedList' | 'blockquote'
  | 'link' | 'unlink' | 'undo' | 'redo' | 'clearFormat'
  | 'alignLeft' | 'alignCenter' | 'alignRight'
  | 'color' | 'highlight' | 'fontFamily' | 'fontSize';

export interface RichTextEngineOptions {
  /** 空内容占位文案 */
  placeholder: string;
  /** 初始 HTML（须已由组件按能力画像预处理） */
  initialHtml: string;
  /** 内容变化回调（html 为空串表示无可见内容） */
  onChange: (html: string) => void;
  /** 选区/状态变化回调（用于刷新工具栏激活态与当前值） */
  onState: () => void;
}

export abstract class RichTextEngine {
  /** 挂载到宿主元素并初始化编辑器 */
  abstract create(host: HTMLElement, options: RichTextEngineOptions): void;
  /** 卸载并销毁 */
  abstract destroy(): void;
  /** 当前内容 HTML（空则返回空串） */
  abstract getHtml(): string;
  /** 由外部设置内容（如父组件恢复初值），不触发 onChange */
  abstract setHtml(html: string): void;
  /** 执行动作；带 value 的动作用于 link/color/highlight/fontFamily/fontSize */
  abstract exec(action: RichTextAction, value?: string): void;
  /** 开关型动作当前是否激活 */
  abstract isActive(action: RichTextAction): boolean;
  /** 值型动作的当前值（color/highlight/fontFamily/fontSize），未应用返回 '' */
  abstract getMarkValue(action: 'color' | 'highlight' | 'fontFamily' | 'fontSize'): string;
}
