# Full-featured rich text editor behind an engine abstraction

## Status

Accepted

## Context

ADR-0009 引入了 TipTap 富文本正文（HTML 存储、服务端 allow-list、可见字数 ≤4000），但：
- 编辑器只在组件里**硬编码具体实现**（组件直接 import TipTap），与 ADR-0007“客户端模块化、可替换”的取向不符；
- 能力集很小（无字体/字号/颜色/高亮/下划线/对齐），产品要求更“全功能”的排版（字体、颜色、大小等）。

## Decision

- **编辑器能力扩展**：在 TipTap v3 上启用 加粗/斜体/下划线/删除线/二级/三级标题/无序与有序列表/引用/链接，以及 **字体族、字号、文字颜色、背景高亮、文本对齐（左/中/右）**、清除格式、撤销/重做。**仍不内嵌图片/表格/代码块/媒体**。
- **抽象优先（ADR-0010 核心）**：新增引擎抽象 `RichTextEngine`（`create/destroy/getHtml/setHtml/exec/isActive/getMarkValue`）+ 动作类型；编辑器外壳组件只依赖该接口与**能力画像**（`RICH_TEXT_PROFILE`、`FONT_FAMILIES`、`FONT_SIZES`），**不 import 任何具体编辑器**。TipTap 实现（`TiptapRichTextEngine`）通过组件 providers 注入，并随懒加载 chunk 一起承载（不进主包）。换 Quill/CKEditor = 新增一个实现 + 替换 providers，页面零改动。
- **行内样式 via style + 能力画像白名单**：允许子集（标签 `p,br,strong,em,u,s,span,mark,h2,h3,ul,ol,li,blockquote,a`；属性 `href,style`；style 内 CSS 属性 `color, background-color, font-family, font-size, text-align`）作为**单一事实来源**，同时约束：
  - 后端入库消毒（Ganss `RichTextSanitizer` 的 AllowedTags/AllowedAttributes/AllowedCssProperties），
  - 前端读取侧渲染消毒（DOMPurify + `bypassSecurityTrustHtml`，因为 Angular 内建消毒会剥掉 `style`）。
- **渲染**：详情只读富文本经 `sanitizeRichTextToTrusted`（DOMPurify 白名单）后以可信方式渲染，安全保留颜色/字体/字号/对齐。

## Alternatives considered

- **换 Quill/CKEditor/TinyMCE**：列为候选但不采纳——TipTap 已就位、headless 最贴合“抽象 + 懒加载”约束；CKEditor/TinyMCE 另有 GPL/商业授权与体积约束。
- **颜色/字体用 CSS class 而非内联 style**：可避免 style 白名单，但要求维护一套命名 class 主题并让编辑器/渲染共享，复杂度更高；内联 style + 双端 CSS 属性白名单更直接且自包含。
- **渲染继续用 Angular 内建 [innerHTML]**：会剥掉全部 `style`，颜色/字体/字号丢失，不可行。
- **不抽象，继续在组件里 import TipTap**：违背“基于抽象而不是具体”的要求，换引擎要改组件与两处页面。

## Consequences

- 编辑器实现与 UI/页面解耦：页面只依赖 `RichTextEngine` 抽象与能力画像；TipTap 仍是默认实现。
- 正文可带内联排版样式；存储仍是 sanitize 后的 HTML（列 `text` 不变），存量正文（无 style）兼容。
- 能力画像成为前端 schema / 后端消毒 / 客户端渲染三端共用的单一事实来源（跨语言以镜像常量 + 文档维护，见 `RICH_TEXT_PROFILE` 与 `RichTextSanitizer` 注释）。
- 编辑器 chunk 因扩展而变大但保持懒加载；新增前端依赖：`@tiptap/extension-underline|text-align|text-style`、`dompurify`。
- 扩展 ADR-0009 的编辑器能力与消毒口径（现由 ADR-0010 覆盖排版集与抽象层）。
