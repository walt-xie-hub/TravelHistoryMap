import { Editor, type Extensions } from '@tiptap/core';
import Placeholder from '@tiptap/extension-placeholder';
import StarterKit from '@tiptap/starter-kit';
import TextAlign from '@tiptap/extension-text-align';
import { BackgroundColor, Color, FontFamily, FontSize, TextStyle } from '@tiptap/extension-text-style';
import type { RichTextAction, RichTextEngineOptions } from './rich-text-engine';
import { RichTextEngine } from './rich-text-engine';

/**
 * 具体编辑器引擎：TipTap v3 实现（ADR-0010）。
 * 本文件是唯一 import TipTap 的地方；组件/页面只依赖 RichTextEngine 抽象。
 * 换引擎 = 另写一个 RichTextEngine 实现并替换组件 providers。
 */
export class TiptapRichTextEngine extends RichTextEngine {
  private editor: Editor | null = null;
  private onChange: (html: string) => void = () => {};

  create(host: HTMLElement, options: RichTextEngineOptions): void {
    this.onChange = options.onChange;
    const editor = new Editor({
      element: host,
      extensions: this.buildExtensions(options.placeholder),
      content: options.initialHtml,
      autofocus: false,
      onUpdate: () => this.emit(),
      onTransaction: () => options.onState(),
    });
    this.editor = editor;
  }

  destroy(): void {
    this.editor?.destroy();
    this.editor = null;
  }

  getHtml(): string {
    const editor = this.editor;
    if (!editor) return '';
    return editor.state.doc.textContent.trim().length === 0 ? '' : editor.getHTML();
  }

  setHtml(html: string): void {
    this.editor?.commands.setContent(html, { emitUpdate: false });
  }

  exec(action: RichTextAction, value?: string): void {
    const editor = this.editor;
    if (!editor) return;
    const chain = editor.chain().focus();
    switch (action) {
      case 'bold': chain.toggleBold(); break;
      case 'italic': chain.toggleItalic(); break;
      case 'underline': chain.toggleUnderline(); break;
      case 'strike': chain.toggleStrike(); break;
      case 'paragraph': chain.setParagraph(); break;
      case 'h2': chain.toggleHeading({ level: 2 }); break;
      case 'h3': chain.toggleHeading({ level: 3 }); break;
      case 'bulletList': chain.toggleBulletList(); break;
      case 'orderedList': chain.toggleOrderedList(); break;
      case 'blockquote': chain.toggleBlockquote(); break;
      case 'link':
        if (value) chain.extendMarkRange('link').setLink({ href: value });
        else chain.extendMarkRange('link').unsetLink();
        break;
      case 'unlink': chain.unsetLink(); break;
      case 'undo': chain.undo(); break;
      case 'redo': chain.redo(); break;
      case 'clearFormat': chain.clearNodes().unsetAllMarks(); break;
      case 'alignLeft': chain.setTextAlign('left'); break;
      case 'alignCenter': chain.setTextAlign('center'); break;
      case 'alignRight': chain.setTextAlign('right'); break;
      case 'color': (value ? chain.setColor(value) : chain.unsetColor()); break;
      case 'highlight': (value ? chain.setBackgroundColor(value) : chain.unsetBackgroundColor()); break;
      case 'fontFamily': (value ? chain.setFontFamily(value) : chain.unsetFontFamily()); break;
      case 'fontSize': (value ? chain.setFontSize(value) : chain.unsetFontSize()); break;
    }
    chain.run();
  }

  isActive(action: RichTextAction): boolean {
    const editor = this.editor;
    if (!editor) return false;
    switch (action) {
      case 'bold': return editor.isActive('bold');
      case 'italic': return editor.isActive('italic');
      case 'underline': return editor.isActive('underline');
      case 'strike': return editor.isActive('strike');
      case 'paragraph': return editor.isActive('paragraph');
      case 'h2': return editor.isActive('heading', { level: 2 });
      case 'h3': return editor.isActive('heading', { level: 3 });
      case 'bulletList': return editor.isActive('bulletList');
      case 'orderedList': return editor.isActive('orderedList');
      case 'blockquote': return editor.isActive('blockquote');
      case 'link': return editor.isActive('link');
      case 'alignLeft': return editor.isActive({ textAlign: 'left' });
      case 'alignCenter': return editor.isActive({ textAlign: 'center' });
      case 'alignRight': return editor.isActive({ textAlign: 'right' });
      default: return false;
    }
  }

  getMarkValue(action: 'color' | 'highlight' | 'fontFamily' | 'fontSize'): string {
    const editor = this.editor;
    if (!editor) return '';
    const key =
      action === 'color' ? 'color'
      : action === 'highlight' ? 'backgroundColor'
      : action === 'fontFamily' ? 'fontFamily'
      : 'fontSize';
    const attrs = editor.getAttributes('textStyle') as Record<string, unknown>;
    const value = attrs[key];
    return typeof value === 'string' ? value : '';
  }

  private emit(): void {
    this.onChange(this.getHtml());
  }

  private buildExtensions(placeholder: string): Extensions {
    return [
      StarterKit.configure({
        heading: { levels: [2, 3] },
        code: false,
        codeBlock: false,
        horizontalRule: false,
        link: { openOnClick: false, autolink: true, defaultProtocol: 'https' },
      }),
      TextStyle,
      Color,
      BackgroundColor,
      FontFamily,
      FontSize,
      TextAlign.configure({ types: ['heading', 'paragraph'] }),
      Placeholder.configure({ placeholder }),
    ];
  }
}
