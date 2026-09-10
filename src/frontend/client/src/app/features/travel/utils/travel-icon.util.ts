/**
 * 旅行标识图标库与地区特色映射（ADR-0016）。
 *
 * - Icon library：前端只读资产（`public/icons/{category}/{key}.svg`），本文件是 key/标签/分类的唯一事实来源。
 * - Regional icon：按 Travel record 的 City snapshot 派生的地区特色图标（渲染期计算，不落库）。
 * - 优先级：显式选择（record.iconKey）> 地区特色（city）> 无（回退默认标记）。
 */

export type TravelIconCategory = 'animal' | 'food' | 'architecture' | 'plant';

export interface TravelIcon {
  key: string;
  label: string;
  category: TravelIconCategory;
}

/** 解析图标所需的记录字段（Travel record 的 icon 快照 + city 快照） */
export interface TravelIconSource {
  iconKey?: string | null;
  city?: string | null;
}

export const TRAVEL_ICONS: readonly TravelIcon[] = [
  // 动物
  { key: 'animal-panda', label: '熊猫', category: 'animal' },
  { key: 'animal-tiger', label: '东北虎', category: 'animal' },
  { key: 'animal-crane', label: '丹顶鹤', category: 'animal' },
  { key: 'animal-snow-leopard', label: '雪豹', category: 'animal' },
  { key: 'animal-antelope', label: '藏羚羊', category: 'animal' },
  { key: 'animal-peacock', label: '孔雀', category: 'animal' },
  { key: 'animal-camel', label: '骆驼', category: 'animal' },
  { key: 'animal-golden-monkey', label: '金丝猴', category: 'animal' },
  { key: 'animal-yak', label: '牦牛', category: 'animal' },
  { key: 'animal-deer', label: '麋鹿', category: 'animal' },
  // 美食
  { key: 'food-hotpot', label: '火锅', category: 'food' },
  { key: 'food-dimsum', label: '早茶点心', category: 'food' },
  { key: 'food-xiaolongbao', label: '小笼包', category: 'food' },
  { key: 'food-roast-duck', label: '烤鸭', category: 'food' },
  { key: 'food-lamian', label: '拉面', category: 'food' },
  { key: 'food-skewer', label: '羊肉串', category: 'food' },
  { key: 'food-rice-noodle', label: '米粉', category: 'food' },
  { key: 'food-tea', label: '茶', category: 'food' },
  { key: 'food-mooncake', label: '月饼', category: 'food' },
  { key: 'food-dumpling', label: '饺子', category: 'food' },
  // 建筑
  { key: 'architecture-great-wall', label: '长城', category: 'architecture' },
  { key: 'architecture-tiananmen', label: '天安门', category: 'architecture' },
  { key: 'architecture-oriental-pearl', label: '东方明珠', category: 'architecture' },
  { key: 'architecture-tulou', label: '土楼', category: 'architecture' },
  { key: 'architecture-palace', label: '故宫', category: 'architecture' },
  { key: 'architecture-pagoda', label: '古塔', category: 'architecture' },
  { key: 'architecture-arcade', label: '骑楼', category: 'architecture' },
  { key: 'architecture-cave-house', label: '窑洞', category: 'architecture' },
  { key: 'architecture-bridge', label: '古桥', category: 'architecture' },
  { key: 'architecture-temple', label: '寺庙', category: 'architecture' },
  // 植物（含市花）
  { key: 'plant-plum-blossom', label: '梅花', category: 'plant' },
  { key: 'plant-peony', label: '牡丹', category: 'plant' },
  { key: 'plant-osmanthus', label: '桂花', category: 'plant' },
  { key: 'plant-lotus', label: '荷花', category: 'plant' },
  { key: 'plant-chrysanthemum', label: '菊花', category: 'plant' },
  { key: 'plant-rose', label: '月季', category: 'plant' },
  { key: 'plant-kapok', label: '木棉', category: 'plant' },
  { key: 'plant-magnolia', label: '白玉兰', category: 'plant' },
  { key: 'plant-orchid', label: '兰花', category: 'plant' },
  { key: 'plant-bamboo', label: '竹', category: 'plant' },
  { key: 'plant-azalea', label: '杜鹃', category: 'plant' },
  { key: 'plant-camellia', label: '山茶花', category: 'plant' },
  { key: 'plant-bougainvillea', label: '三角梅', category: 'plant' },
  { key: 'plant-lilac', label: '丁香', category: 'plant' },
  { key: 'plant-jasmine', label: '茉莉', category: 'plant' },
  { key: 'plant-hibiscus', label: '木芙蓉', category: 'plant' },
  { key: 'plant-pomegranate-flower', label: '石榴花', category: 'plant' },
];

const BY_KEY = new Map(TRAVEL_ICONS.map((icon) => [icon.key, icon]));

/** 按 key 取图标；未知/空返回 undefined（渲染时回退） */
export function travelIcon(key: string | null | undefined): TravelIcon | undefined {
  return key ? BY_KEY.get(key.trim()) : undefined;
}

/** 图标资源的公开路径（`public/icons/...`） */
export function iconFile(icon: TravelIcon): string {
  return `/icons/${icon.category}/${icon.key}.svg`;
}

/**
 * 地区特色映射（MVP 精选，约 30 条热门城市；其余城市回退默认）。
 * 城市去行政后缀后匹配（如 “上海市”→“上海”）。
 * 准确度以公开资料为准，后续可按需补全到全国地级市（见 ADR-0016）。
 */
const REGIONAL_ICON_KEYS: Readonly<Record<string, string>> = {
  上海: 'plant-magnolia',
  北京: 'architecture-tiananmen',
  广州: 'plant-kapok',
  成都: 'animal-panda',
  重庆: 'food-hotpot',
  西安: 'plant-pomegranate-flower',
  洛阳: 'plant-peony',
  杭州: 'plant-osmanthus',
  苏州: 'plant-osmanthus',
  合肥: 'plant-osmanthus',
  桂林: 'plant-osmanthus',
  南京: 'plant-plum-blossom',
  武汉: 'plant-plum-blossom',
  长沙: 'plant-azalea',
  昆明: 'plant-camellia',
  宁波: 'plant-camellia',
  温州: 'plant-camellia',
  青岛: 'plant-camellia',
  厦门: 'plant-bougainvillea',
  海口: 'plant-bougainvillea',
  三亚: 'plant-bougainvillea',
  福州: 'plant-jasmine',
  哈尔滨: 'plant-lilac',
  西宁: 'plant-lilac',
  呼和浩特: 'plant-lilac',
  济南: 'plant-lotus',
  太原: 'plant-chrysanthemum',
  石家庄: 'plant-rose',
  郑州: 'plant-rose',
  天津: 'plant-rose',
  兰州: 'plant-rose',
  沈阳: 'plant-rose',
  大连: 'plant-rose',
  乌鲁木齐: 'plant-rose',
  拉萨: 'animal-antelope',
  贵阳: 'plant-orchid',
};

function normalizeCityName(city: string): string {
  return city.trim().replace(/(自治州|地区|盟|市)$/, '');
}

/** 按城市短名取地区特色 icon key（未收录/空 → undefined） */
export function regionalIconKey(city: string | null | undefined): string | undefined {
  if (!city) return undefined;
  const trimmed = city.trim();
  if (!trimmed) return undefined;
  const key = REGIONAL_ICON_KEYS[normalizeCityName(trimmed)] ?? REGIONAL_ICON_KEYS[trimmed];
  return travelIcon(key)?.key;
}

/** 解析一条记录最终应显示的图标 key：显式选择 > 地区特色 > 无（ADR-0016） */
export function resolveIconKey(record: TravelIconSource): string | undefined {
  return travelIcon(record.iconKey)?.key ?? regionalIconKey(record.city);
}

/** 一条记录最终应显示的图标对象（显式 > 地区 > 无） */
export function iconForRecord(record: TravelIconSource): TravelIcon | undefined {
  return travelIcon(resolveIconKey(record));
}

/** 一条记录的图标资源路径；无图标时 undefined（各展示面统一入口，避免重复 record→path 逻辑） */
export function iconSrcForRecord(record: TravelIconSource): string | undefined {
  const icon = iconForRecord(record);
  return icon ? iconFile(icon) : undefined;
}

/** 地区特色模块展示用：所有被地区映射引用的 icon key（去重，按库内顺序） */
export function regionalIconKeys(): readonly string[] {
  const used = new Set(Object.values(REGIONAL_ICON_KEYS));
  return TRAVEL_ICONS.filter((icon) => used.has(icon.key)).map((icon) => icon.key);
}

/** 反向查询：关联到该地区特色图标的城市名（地区 tab 显示「城市 → 图标」关联） */
export function regionalCitiesOf(key: string): readonly string[] {
  return Object.entries(REGIONAL_ICON_KEYS)
    .filter(([, value]) => value === key)
    .map(([city]) => city);
}

/** 一条 City run 的共享图标：组内解析结果全相同才返回，否则 undefined（聚合标记回退，ADR-0016） */
export function sharedTravelIcon(records: readonly TravelIconSource[]): TravelIcon | undefined {
  const first = records[0] ? iconForRecord(records[0]) : undefined;
  if (!first) return undefined;
  return records.every((record) => iconForRecord(record)?.key === first.key) ? first : undefined;
}
