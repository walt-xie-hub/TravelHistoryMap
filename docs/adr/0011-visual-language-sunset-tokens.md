# Visual language: sunset palette, tokens, and unified interaction states

## Status

Accepted

## Context

产品要求全站风格统一、简约但色彩鲜明，并让输入与数据展示更友好。此前主题接近 Tailwind 默认灰阶 + 蓝色主色（`#2563eb`），语义色分散：错误红/成功绿硬编码在多个页面 scss，地图 marker 三态色与主色不共享变量，甚至图例圆点没有样式、不可见。

## Decision

- **视觉方向选“日落活力”（暖色鲜明）**：主色珊瑚橙 `#ea580c`（strong `#c2410c`，soft `#fff3ea`），暖纸白中性底（`--bg #faf5ef`、白卡片、暖灰边框与文字），语义色：
  - 成功 绿 `#059669`（soft/border），危险 红 `#dc2626`（soft/border），警告 琥珀 `#d97706`；
  - 地图三态统一为主题语义色：普通 = 珊瑚橙、进行中 = 绿、多次到访 = 紫 `#7c3aed`。
- **token 体系化**：语义/几何 token 集中在 `styles.scss :root`（`--primary*`、`--danger*`、`--success*`、`--warning*`、`--radius-*`、`--shadow-*`、`--focus-ring`）；页面硬编码的错误红/成功绿等收编为 token。绝大多数组件本就使用 `var(--*, fallback)`，改 `:root` 即全站生效。
- **保留地图卡通 marker 造型**，仅重着色；补齐地图**图例圆点**样式（此前无样式不可见）。
- **统一焦点态**：全局 `:focus-visible` 用主色 focus ring；编辑器等有自定义 outline 规则处自行覆盖（避免回到“黑边”问题）。
- **字体与排版**：拉丁/数字用 `Inter`，中文走系统字体栈（`PingFang SC / Microsoft YaHei / Noto Sans SC`，不引外部字体）；数字统一 `font-variant-numeric: tabular-nums`；补 `--font-sans` 与 `--text-xs…2xl`、行高 token。
- **布局 token 化**：`--header-h / --content-pad-* / --container-max|lg|md / --side-panel-w / --map-chrome` 收编头部高度、内容区宽度/内边距、创建/详情容器宽度、侧栏宽度与地图主区高度（原 `calc(100dvh-220px)` 魔法数改为 token 表达），保持响应式断点语义。
- **数据展示友好化（深化）**：侧栏“统计”升级为语义色小卡；列表空态加图标引导；侧栏/气泡时间显示人性化（同年省略年份的 `M月d日 HH:mm`、同日区间只写时刻、跨年保留年份，经 `travel-display.fmtDateTime`）。
- **只做亮色一套**，本期不做深色模式。

## Alternatives considered

- **其它候选主题**（海岸青/森林沙/靛蓝莓/极简霓虹）：在 grill 中列给用户选，用户采纳推荐的“日落活力”。
- **更换 marker 为简约圆点/图钉**：被否决——保留卡通标识的识别性，仅统一配色。
- **仅换色板不 token 化**：被否决——会留下分散硬编码，后续换肤要逐页改。
- **做深色模式**：范围膨胀，本期不做。

## Consequences

- 全站风格统一到单一 token 源；改主题只需编辑 `styles.scss :root`。
- 错误/成功/警告语义有了统一 token（auth、表单、地图搜索错误、详情/创建页计数溢出、图片删除悬停等已收编）。
- 地图 marker 语义色与图例一致（普通橙/进行中绿/多次紫），图例可见。
- 输入/按钮获得一致的主色焦点环与配色，视觉“鲜明且简约”。
- 纯视觉改动，不改数据/API；无领域词汇影响。
