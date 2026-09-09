# Rich text editor for travel detail (HTML storage)

## Status

Accepted

## Context

Travel detail 此前是单条**纯文本**描述（`Description varchar(4000)`，详情页 `pre-wrap` 渲染，添加页/详情编辑态用 `<textarea maxlength=4000>`，ADR-0006/0008）。产品认为正文“太简单”，需要一个真正的编辑器来排版正文（加粗/标题/列表/引用/链接等）。

约束与背景：
- 客户端此前零编辑器/渲染依赖；后端零 HTML 消毒依赖。
- 已有存量纯文本 Description（不含任何 HTML 标签），且**没有迁移/版本机制**。
- 内容只属于登录用户本人（ADR-0005 归属隔离），但仍需消毒以防自 XSS 与脏数据。

## Decision

- **格式定为富文本所见即所得，HTML 存储**。编辑器用 **TipTap（v3）**（框架无关、贴合 Angular standalone），StarterKit 子集 + Link + Placeholder；无内嵌图片。
- **可允许排版子集**（前后端一致，防 XSS allow-list）：`p, br, strong, em, h2, h3, ul, ol, li, blockquote, a[href]`；**不加粗外标记、无代码、无水平线、无图片**。编辑器 schema 关掉 `strike/code/codeBlock/horizontalRule`，使产出与白名单严格一致。
- **入库前服务端消毒**（HtmlSanitizer/Ganss，Travel.Application 内 `RichTextSanitizer`）：标签/属性 allow-list，`href` 仅允许 `http/https/mailto`，剥离 class/style/事件属性与危险协议；不可见空内容归一化为 `null`。
- **长度口径改为“可见字符 ≤4000”**：去标签 → HTML 实体解码 → 连续空白折叠为一个空格后计数（前后端同口径）；超限返回 400。`Description` 列由 `varchar(4000)` **扩为 `text`**（EF 模型 + 幂等 `ALTER COLUMN TYPE text`），不再以原始长度截断。
- **存量兼容（不迁移）**：旧纯文本 Description 不含块级标签，渲染层检测后按纯文本安全显示（保持原样）；进入编辑器加载时按段落转 HTML，**保存一次即升级为富文本**。
- **展示/编辑覆盖**：添加旅游记录页与详情页编辑态共用同一 `app-rich-text-editor`；详情页只读对富文本以 `[innerHTML]`（Angular 内建消毒兜底）按排版渲染；地图气泡/侧栏不展示正文。
- 字数统计随编辑器实时显示（N/4000），超限前端阻止保存、后端仍兜底校验。

## Alternatives considered

- **Markdown 存储/编辑**：被否决——目标用户是普通旅行者，所见即所得工具栏更符合“编辑器”预期；Markdown 符号对中文书写不友好。纯文本存量虽天然兼容 Markdown，但牺牲了直觉交互。
- **仅放大纯文本编辑区（不格式化）**：被否决——不解决“文本太简单”的能力诉求。
- **富文本允许内嵌图片 / JSON 文档存储**：被否决——图片已有独立 Travel image 九图区，内嵌图会引入第二条上传/渲染/消毒链路；JSON 存储同样复杂化且让旧数据与第三方渲染更难。
- **一次性迁移存量文本为 HTML**：被否决——无迁移基建，采用渲染/编辑入口按需兼容（决策的 (i) 路线）。

## Consequences

- 新建/编辑的正文成为有排版的 HTML；旧纯文本记录显示不变，一旦编辑保存即升级。
- 引入两处新依赖：前端 `@tiptap/*`（编辑器懒加载 chunk），后端 `HtmlSanitizer`。
- 客户端渲染依赖 Angular 内建消毒 + 服务端入库消毒的双保险；正文不承载图片。
- 长度语义由“原始 4000 字符”变为“可见 ≤4000 字”，`Description` 列变为 `text`（对已部署库由 DatabaseInitializer 幂等扩宽）。
- 部分澄清 ADR-0006/0008 的“纯文本描述”表述，并为 CONTEXT 的 **Travel detail** 更新“带排版的正文、不内嵌图片”语义。
