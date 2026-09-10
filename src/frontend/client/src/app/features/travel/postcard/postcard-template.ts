/**
 * 明信片模板注册表（ADR-0018）——模板标准的唯一事实来源。
 *
 * 明信片是对一条（或多条）Travel record 的**本地只读呈现**：竖版 100×148mm @300dpi = 1181×1748。
 * 模板不只是一层皮：它同时声明**用几张图、支持哪些模块、清单最多几行**，
 * 因此用户可以按模板的能力范围开关模块，而不会把版式撑破。
 *
 * 两个维度组合出 10 款：版式族（正面式：照片是主角；背面式：纸面 + 收件人栏）
 * × 风格（复古纸 / 手账 / 极简 / 现代）。
 *
 * **声明即可用**：`modules` 里写了的模块，该版式就必须真的给它留了落点
 * （例：只有经典邮政 / 航空邮简 / 拍立得 / 拼贴铁盒 有「旅行标识」位置，其余 6 款不声明它）。
 * 否则开关会变成空操作，用户会在面板里看到一个永远没效果的按钮。
 *
 * 本文件是纯数据 + 纯函数，不依赖 Angular，可被 vitest 直接覆盖。
 */

/** 明信片画布尺寸（100×148mm @300dpi） */
export const POSTCARD_WIDTH = 1181;
export const POSTCARD_HEIGHT = 1748;

/**
 * 可开关的模块：
 * - `trail` 足迹清单（仅多条记录时出现；行数上限由模板声明）
 * - `icon` 旅行标识图标（`iconForRecord` 的剪影，解析为空时自动不渲染）
 * - `coord` 坐标小字（经纬度）
 * - `stamp` 邮票
 * - `postmark` 邮戳（圆形日戳，压在邮票一角）
 * - `recipient` 收件人 / 地址 / 邮编栏（仅背面式）
 * - `caption` 地点与日期行
 */
export type PostcardModuleId =
  | 'trail'
  | 'icon'
  | 'coord'
  | 'stamp'
  | 'postmark'
  | 'recipient'
  | 'caption';

/** 模块的中文名（面板里显示，也是文档对照用的唯一措辞） */
export const POSTCARD_MODULE_LABELS: Readonly<Record<PostcardModuleId, string>> = {
  trail: '足迹清单',
  icon: '旅行标识',
  coord: '坐标小字',
  stamp: '邮票',
  postmark: '邮戳',
  recipient: '收件人栏',
  caption: '地点日期',
};

/** 版式族：正面式＝照片是主角；背面式＝纸面 + 收件人栏（真正能寄的那一面） */
export type PostcardLayout = 'front' | 'back';

export type PostcardStyle = 'vintage' | 'journal' | 'minimal' | 'modern';

export interface PostcardTemplate {
  readonly id: string;
  readonly name: string;
  readonly layout: PostcardLayout;
  readonly style: PostcardStyle;
  /** 需要的图片张数：0 表示纯文字版式 */
  readonly imageCount: number;
  /** 足迹清单的行数上限（模板自己最清楚能放几行） */
  readonly trailLimit: number;
  /** 该模板**支持**的模块（用户只能在这个集合内开关） */
  readonly modules: readonly PostcardModuleId[];
  /** 该模板**默认开启**的模块（一定是 `modules` 的子集） */
  readonly defaults: readonly PostcardModuleId[];
}

/**
 * 10 款模板（ADR-0018 的标准表）。
 * 顺序即面板里的展示顺序：先把常见品类放前面，背面式收尾。
 */
export const POSTCARD_TEMPLATES: readonly PostcardTemplate[] = [
  {
    id: 'classic-post',
    name: '经典邮政',
    layout: 'front',
    style: 'vintage',
    imageCount: 1,
    trailLimit: 3,
    modules: ['caption', 'stamp', 'postmark', 'coord', 'trail', 'icon'],
    defaults: ['caption', 'stamp', 'postmark', 'coord'],
  },
  {
    id: 'air-mail',
    name: '航空邮简',
    layout: 'front',
    style: 'vintage',
    imageCount: 1,
    trailLimit: 3,
    modules: ['caption', 'stamp', 'postmark', 'trail', 'icon'],
    defaults: ['caption', 'stamp', 'postmark'],
  },
  {
    id: 'polaroid',
    name: '拍立得',
    layout: 'front',
    style: 'journal',
    imageCount: 1,
    trailLimit: 5,
    modules: ['caption', 'stamp', 'postmark', 'trail', 'icon'],
    defaults: ['caption', 'stamp', 'postmark'],
  },
  {
    id: 'collage',
    name: '拼贴铁盒',
    layout: 'front',
    style: 'journal',
    imageCount: 3,
    trailLimit: 5,
    modules: ['caption', 'stamp', 'postmark', 'trail', 'icon'],
    defaults: ['caption', 'stamp', 'postmark', 'trail', 'icon'],
  },
  {
    id: 'pinned-map',
    name: '图钉地图',
    layout: 'front',
    style: 'vintage',
    imageCount: 2,
    trailLimit: 4,
    modules: ['caption', 'stamp', 'postmark', 'coord', 'trail'],
    defaults: ['caption', 'stamp', 'postmark', 'coord', 'trail'],
  },
  {
    id: 'ink-minimal',
    name: '极简墨白',
    layout: 'front',
    style: 'minimal',
    imageCount: 1,
    trailLimit: 3,
    modules: ['caption', 'stamp', 'postmark', 'trail'],
    defaults: ['caption', 'stamp', 'postmark'],
  },
  {
    id: 'film-strip',
    name: '胶片条',
    layout: 'front',
    style: 'minimal',
    imageCount: 2,
    trailLimit: 3,
    modules: ['caption', 'stamp', 'postmark', 'trail'],
    defaults: ['caption', 'stamp', 'postmark'],
  },
  {
    id: 'sunrise',
    name: '日出渐变',
    layout: 'front',
    style: 'modern',
    imageCount: 1,
    trailLimit: 3,
    modules: ['caption', 'stamp', 'postmark', 'trail'],
    defaults: ['caption', 'stamp', 'postmark'],
  },
  {
    id: 'back-letter',
    name: '背面·写信',
    layout: 'back',
    style: 'vintage',
    imageCount: 1,
    trailLimit: 4,
    modules: ['caption', 'stamp', 'postmark', 'recipient', 'trail'],
    defaults: ['caption', 'stamp', 'postmark', 'recipient', 'trail'],
  },
  {
    id: 'back-plain',
    name: '背面·纯文',
    layout: 'back',
    style: 'minimal',
    imageCount: 0,
    trailLimit: 5,
    modules: ['caption', 'stamp', 'postmark', 'recipient', 'trail'],
    defaults: ['caption', 'stamp', 'postmark', 'recipient', 'trail'],
  },
];

/** 默认模板（打开明信片编辑器时的初选） */
export const DEFAULT_POSTCARD_TEMPLATE_ID = 'classic-post';

export function postcardTemplateById(id: string): PostcardTemplate {
  return (
    POSTCARD_TEMPLATES.find((template) => template.id === id) ??
    POSTCARD_TEMPLATES.find((template) => template.id === DEFAULT_POSTCARD_TEMPLATE_ID)!
  );
}

/** 该模板是否支持某模块 */
export function templateSupports(template: PostcardTemplate, moduleId: PostcardModuleId): boolean {
  return template.modules.includes(moduleId);
}

/**
 * 按模板能力收敛模块开关：
 * - 模板**支持**且用户**开了** → 开；
 * - 模板支持的模块没有用户意见时用模板默认值；
 * - 模板**不支持**的一律关掉（换模板时不会把版式撑破）。
 *
 * 传入 `previous`（用户在当前会话里的选择）即可实现"换模板保留已支持的开关"。
 */
export function resolveModules(
  template: PostcardTemplate,
  previous?: Readonly<Partial<Record<PostcardModuleId, boolean>>>,
): Record<PostcardModuleId, boolean> {
  const resolved = {} as Record<PostcardModuleId, boolean>;
  for (const moduleId of Object.keys(POSTCARD_MODULE_LABELS) as PostcardModuleId[]) {
    if (!templateSupports(template, moduleId)) {
      resolved[moduleId] = false;
      continue;
    }
    const userChoice = previous?.[moduleId];
    resolved[moduleId] = userChoice ?? template.defaults.includes(moduleId);
  }
  return resolved;
}

/** 该模板可切换的模块列表（面板里按这个顺序渲染开关） */
export function switchableModules(template: PostcardTemplate): readonly PostcardModuleId[] {
  return (Object.keys(POSTCARD_MODULE_LABELS) as PostcardModuleId[]).filter((moduleId) =>
    templateSupports(template, moduleId),
  );
}

/**
 * 从"当前开关 + 当前模板默认值"反推出**用户真正改过**的开关。
 *
 * 为什么需要它：换模板时若把上一个模板的**默认值**当成用户意见，
 * 就会出现"切到天生带足迹清单的模板，清单却是关的"这种反直觉结果。
 * 只有与当前模板默认值不同的项才算用户意见，其余留给新模板的默认值决定。
 */
export function userModuleOverrides(
  template: PostcardTemplate,
  current: Readonly<Partial<Record<PostcardModuleId, boolean>>>,
): Partial<Record<PostcardModuleId, boolean>> {
  const overrides: Partial<Record<PostcardModuleId, boolean>> = {};
  for (const moduleId of Object.keys(POSTCARD_MODULE_LABELS) as PostcardModuleId[]) {
    const value = current[moduleId];
    if (value === undefined) continue;
    if (value !== template.defaults.includes(moduleId)) overrides[moduleId] = value;
  }
  return overrides;
}
