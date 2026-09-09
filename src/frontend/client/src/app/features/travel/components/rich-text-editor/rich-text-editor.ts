import { AfterViewInit, ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, OnDestroy, effect, inject, input, output, signal, type WritableSignal, viewChild } from '@angular/core';
import { FONT_FAMILIES, FONT_SIZES } from '../../rich-text/rich-text-profile';
import { RichTextAction, RichTextEngine } from '../../rich-text/rich-text-engine';
import { TiptapRichTextEngine } from '../../rich-text/tiptap-rich-text-engine';
import { isRichHtml, plainTextToHtml } from '../../utils/rich-text.util';

/**
 * Travel detail 正文富文本编辑器外壳（ADR-0009/0010）。
 * 组件只依赖 RichTextEngine 抽象 + 能力画像（FONT_FAMILIES/FONT_SIZES），不 import 具体编辑器；
 * TipTap 引擎经 providers 注入（本组件懒加载 chunk 承载其实现）。
 * 输出 sanitize 前的 HTML 给父组件；入库前由后端再次消毒与做可见字数 ≤4000 校验。
 * 兼容存量纯文本：加载时按段落转换成 HTML；空内容对外输出空串。
 */
@Component({
  selector: 'app-rich-text-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: RichTextEngine, useFactory: () => new TiptapRichTextEngine() }],
  imports: [],
  templateUrl: './rich-text-editor.html',
  styleUrl: './rich-text-editor.scss',
})
export class RichTextEditorComponent implements AfterViewInit, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly engine = inject(RichTextEngine);
  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');

  /** 当前 HTML 内容（外部写入；由 valueChange 回传）。 */
  readonly value = input<string>('');
  /** 空内容时的占位提示。 */
  readonly placeholder = input<string>('记录这段旅行的见闻…');
  readonly valueChange = output<string>();

  protected readonly fonts = FONT_FAMILIES;
  protected readonly sizes = FONT_SIZES;

  // 工具栏激活态
  protected readonly bold = signal(false);
  protected readonly italic = signal(false);
  protected readonly underline = signal(false);
  protected readonly strike = signal(false);
  protected readonly heading2 = signal(false);
  protected readonly heading3 = signal(false);
  protected readonly bullet = signal(false);
  protected readonly ordered = signal(false);
  protected readonly quote = signal(false);
  protected readonly link = signal(false);
  protected readonly alignLeft = signal(false);
  protected readonly alignCenter = signal(false);
  protected readonly alignRight = signal(false);
  protected readonly color = signal('#000000');
  protected readonly highlight = signal('#ffff00');
  protected readonly fontFamily = signal('');
  protected readonly fontSize = signal('');

  private created = false;
  private lastEmitted: string | null = null;

  constructor() {
    // 外部值（父组件）变化时同步进编辑器；自己 emit 回传的值跳过，避免回环。
    effect(() => {
      const next = this.value();
      if (!this.created || next === this.lastEmitted) return;
      if (next === this.engine.getHtml()) return;
      this.engine.setHtml(this.toContent(next));
      this.lastEmitted = next;
    });
  }

  ngAfterViewInit(): void {
    this.engine.create(this.host().nativeElement, {
      placeholder: this.placeholder(),
      initialHtml: this.toContent(this.value()),
      onChange: (html) => {
        this.lastEmitted = html;
        this.valueChange.emit(html);
      },
      onState: () => this.refreshToolbar(),
    });
    this.created = true;
    this.refreshToolbar();
    // 将规范化后的内容（旧纯文本→段落）回推给父组件，保证“保存即升级为 HTML”
    queueMicrotask(() => {
      const html = this.engine.getHtml();
      this.lastEmitted = html;
      this.valueChange.emit(html);
    });
  }

  ngOnDestroy(): void {
    this.created = false;
    this.engine.destroy();
  }

  // —— 工具栏动作 ——

  protected preventDefault(event: Event): void {
    // 阻止 mousedown 让工具栏夺走编辑器焦点，从而保留选区（select/color input 除外）
    event.preventDefault();
  }

  protected exec(action: RichTextAction): void {
    this.engine.exec(action);
  }

  protected execValue(action: RichTextAction, value: string): void {
    this.engine.exec(action, value);
  }

  protected setLink(): void {
    const answer = window.prompt('链接地址（http/https/mailto），留空则移除链接：', '');
    if (answer === null) return;
    this.engine.exec('link', answer.trim());
  }

  protected onFontFamily(event: Event): void {
    this.execValue('fontFamily', (event.target as HTMLSelectElement).value);
  }

  protected onFontSize(event: Event): void {
    this.execValue('fontSize', (event.target as HTMLSelectElement).value);
  }

  protected onColor(event: Event): void {
    this.execValue('color', (event.target as HTMLInputElement).value);
  }

  protected onHighlight(event: Event): void {
    this.execValue('highlight', (event.target as HTMLInputElement).value);
  }

  // —— 状态刷新 ——

  private refreshToolbar(): void {
    const toggles: Record<string, WritableSignal<boolean>> = {
      bold: this.bold,
      italic: this.italic,
      underline: this.underline,
      strike: this.strike,
      heading2: this.heading2,
      heading3: this.heading3,
      bullet: this.bullet,
      ordered: this.ordered,
      quote: this.quote,
      link: this.link,
      alignLeft: this.alignLeft,
      alignCenter: this.alignCenter,
      alignRight: this.alignRight,
    };
    for (const [key, setter] of Object.entries(toggles)) {
      setter.set(this.engine.isActive(key as RichTextAction));
    }
    this.color.set(toHex(this.engine.getMarkValue('color')) ?? '#000000');
    this.highlight.set(toHex(this.engine.getMarkValue('highlight')) ?? '#ffff00');
    this.fontFamily.set(this.engine.getMarkValue('fontFamily'));
    this.fontSize.set(this.engine.getMarkValue('fontSize'));
    this.cdr.markForCheck();
  }

  private toContent(raw: string): string {
    if (!raw) return '';
    return isRichHtml(raw) ? raw : plainTextToHtml(raw);
  }
}

/** 把 CSS 颜色（#hex 或 rgb()）归一成 #hex 供 color input 使用；解析失败返回 null。 */
function toHex(cssColor: string): string | null {
  if (!cssColor) return null;
  const trimmed = cssColor.trim();
  if (/^#[0-9a-fA-F]{6}$/.test(trimmed)) return trimmed.toLowerCase();
  if (/^#[0-9a-fA-F]{3}$/.test(trimmed)) {
    return '#' + trimmed.slice(1).split('').map((c) => c + c).join('').toLowerCase();
  }
  const rgb = trimmed.match(/rgba?\(\s*(\d+)[,\s]+(\d+)[,\s]+(\d+)/);
  if (!rgb) return null;
  const [r, g, b] = [rgb[1], rgb[2], rgb[3]].map((n) => Number(n).toString(16).padStart(2, '0'));
  return `#${r}${g}${b}`;
}
