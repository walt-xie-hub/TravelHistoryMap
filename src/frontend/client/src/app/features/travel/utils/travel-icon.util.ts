/**
 * 旅行标识图标库与地区特色映射（ADR-0016）。
 *
 * - 图标是**内联矢量剪影**：48×48 视口、除形体以外全透明、**不含文字、不含底衬**。
 *   颜色一律用 `currentColor`，由使用处的 CSS 决定（地图标记用状态色，其余用中性墨色）。
 * - 本文件是 key / 标签 / 分类 / 造型与地区映射的唯一事实来源；没有独立的资产文件。
 * - 优先级：显式选择（record.iconKey）> 地区特色（city）> 无（回退默认标记）。
 */

export type TravelIconCategory = 'animal' | 'food' | 'architecture' | 'plant';

/**
 * 一个剪影子形。
 * 单个 path 使用偶数环绕序（evenodd）：其内部子路径即镂空（眼窝、门洞、方孔等）。
 * 需要「并集」的重叠形体拆成多个子形（各画一次，天然合并）。
 */
export interface TravelIconShape {
  d: string;
  /** 围绕中心等角复制（花瓣等旋转对称结构） */
  repeat?: { count: number; cx?: number; cy?: number };
}

export interface TravelIcon {
  key: string;
  label: string;
  category: TravelIconCategory;
  /** 剪影主形 */
  shapes: readonly TravelIconShape[];
  /** 线描细节（蒸汽 / 枝干 / 花蕊 / 栏杆），以圆头描边绘制 */
  lines?: readonly string[];
  /** 线描宽度，默认 2.6 */
  lineWidth?: number;
}

type Point = readonly [number, number];

const r2 = (n: number) => Math.round(n * 100) / 100;
/** 圆形 */
const ring = (cx: number, cy: number, r: number) =>
  `M${r2(cx - r)} ${cy}a${r} ${r} 0 1 0 ${r2(2 * r)} 0a${r} ${r} 0 1 0 ${r2(-2 * r)} 0Z`;
/** 椭圆 */
const oval = (cx: number, cy: number, rx: number, ry: number) =>
  `M${r2(cx - rx)} ${cy}a${rx} ${ry} 0 1 0 ${r2(2 * rx)} 0a${rx} ${ry} 0 1 0 ${r2(-2 * rx)} 0Z`;
/** 直角矩形 */
const box = (x: number, y: number, w: number, h: number) => `M${x} ${y}h${w}v${h}h${-w}Z`;
/** 多边形 */
const poly = (points: readonly Point[]) => `M${points.map(([x, y]) => `${x} ${y}`).join('L')}Z`;
/** 朝上的尖瓣（花冠通用件）：根部离中心 baseOffset，长度 length，半宽 halfWidth */
const petal = (length: number, halfWidth: number, baseOffset = 3.6) =>
  `M${r2(24 - halfWidth)} ${r2(24 - baseOffset)}` +
  `Q${r2(24 - halfWidth * 0.9)} ${r2(24 - length * 0.6)} 24 ${r2(24 - length)}` +
  `Q${r2(24 + halfWidth * 0.9)} ${r2(24 - length * 0.6)} ${r2(24 + halfWidth)} ${r2(24 - baseOffset)}` +
  `Q24 ${r2(24 - baseOffset + 4)} ${r2(24 - halfWidth)} ${r2(24 - baseOffset)}Z`;
/** 拱形洞口（建筑通用件）：底边中点在 (cx, bottom)，宽 w、拱高 h */
const arch = (cx: number, bottom: number, w: number, h: number) =>
  `M${r2(cx - w / 2)} ${bottom}v${r2(-h + w / 2)}a${r2(w / 2)} ${r2(w / 2)} 0 0 1 ${w} 0v${r2(h - w / 2)}Z`;

const FIVE = { count: 5 } as const;
const SIX = { count: 6 } as const;

export const TRAVEL_ICONS: readonly TravelIcon[] = [
  // ── 动物 ─────────────────────────────────────────────
  {
    key: 'animal-panda',
    label: '熊猫',
    category: 'animal',
    shapes: [
      { d: ring(12, 13, 7) },
      { d: ring(36, 13, 7) },
      {
        d: `${oval(24, 27, 16, 15)} ${oval(17.5, 24, 4.6, 6)} ${oval(30.5, 24, 4.6, 6)} ${oval(24, 33.4, 2.6, 1.9)}`,
      },
    ],
  },
  {
    key: 'animal-tiger',
    label: '东北虎',
    category: 'animal',
    shapes: [
      { d: ring(11.5, 12.5, 6.4) },
      { d: ring(36.5, 12.5, 6.4) },
      { d: ring(8.5, 33, 4.6) },
      { d: ring(39.5, 33, 4.6) },
      {
        d: `${oval(24, 27, 15, 14.6)} ${oval(19, 16.5, 1.7, 4)} ${oval(24, 15.4, 1.7, 4.4)} ${oval(29, 16.5, 1.7, 4)} ${oval(17.5, 25, 3.6, 4.2)} ${oval(30.5, 25, 3.6, 4.2)} ${oval(24, 32, 2.8, 2)}`,
      },
    ],
    lines: ['M24 34.2v2.6', 'M21 38.4c1.6 1.4 4.4 1.4 6 0'],
  },
  {
    key: 'animal-crane',
    label: '丹顶鹤',
    category: 'animal',
    shapes: [
      { d: `${oval(19, 29, 9.6, 6.6)} ${oval(19, 28.4, 5.2, 2.2)}` },
      { d: 'M26 25c.6-5 2.6-8.6 6.2-10.4l3.2 3.6c-3 1.6-4.4 4.6-4.8 8z' },
      { d: ring(35, 12, 3.4) },
      { d: poly([[38.4, 10.4], [45.6, 12.2], [38.4, 14]]) },
      { d: poly([[10.6, 25], [4, 20], [5.4, 27.8]]) },
    ],
    lines: ['M17.6 35.2v8.8', 'M22.4 35.2v8.8'],
  },
  {
    key: 'animal-snow-leopard',
    label: '雪豹',
    category: 'animal',
    shapes: [
      {
        d: `${oval(21, 30, 11.6, 6.8)} ${ring(18, 28.6, 1.5)} ${ring(24, 31, 1.5)} ${ring(15.6, 31.6, 1.3)}`,
      },
      { d: 'M10.4 31c-5.6 1-8.6-3.2-6.2-6.8l3.4 2c-.8 1.6.4 2.8 2.6 3z' },
      { d: ring(32.6, 24.6, 6.4) },
      { d: poly([[28.6, 19.6], [30.4, 14.4], [33.4, 19.4]]) },
      { d: poly([[35.4, 19.6], [38.8, 14.8], [39.6, 20.6]]) },
    ],
  },
  {
    key: 'animal-antelope',
    label: '藏羚羊',
    category: 'animal',
    shapes: [
      { d: oval(19, 30, 11, 6.4) },
      { d: 'M27 27.4l2.6-9.4 4.4 1.2-2 9.6z' },
      { d: oval(34, 16, 4.6, 3.4) },
    ],
    lines: [
      'M32.6 12.6c-.8-5.6.4-9 3-11.4',
      'M35.8 12.6c1.6-5.2 3.8-7.8 6.6-9.2',
      'M12.6 35v9',
      'M18.4 36v8',
      'M24.4 36v8',
      'M28.6 35v9',
    ],
    lineWidth: 2,
  },
  {
    key: 'animal-peacock',
    label: '孔雀',
    category: 'animal',
    shapes: [
      {
        d: `M6 27a18 18 0 0 1 36 0Z ${ring(24, 15, 2.2)} ${ring(15, 20.4, 2.2)} ${ring(33, 20.4, 2.2)} ${ring(10.4, 25.4, 1.9)} ${ring(37.6, 25.4, 1.9)}`,
      },
      { d: oval(24, 34, 6, 7.6) },
      { d: ring(24, 23.4, 3.2) },
    ],
    lines: ['M24 20.4v-4.6'],
  },
  {
    key: 'animal-camel',
    label: '骆驼',
    category: 'animal',
    shapes: [
      { d: oval(24, 30, 15, 7) },
      { d: ring(16.6, 23, 6) },
      { d: ring(31, 22.6, 6.4) },
      { d: 'M34.6 28l1.6-9.6 4.8-1.4 1.8 4-3.2 1-1.2 6z' },
      { d: oval(41, 14.6, 3.6, 2.8) },
    ],
    lines: ['M11.6 35v9', 'M17.6 35.4v8.6', 'M29.6 35.4v8.6', 'M35.6 35v9'],
  },
  {
    key: 'animal-golden-monkey',
    label: '金丝猴',
    category: 'animal',
    shapes: [
      { d: ring(11.6, 19, 3.4) },
      { d: ring(36.4, 19, 3.4) },
      { d: `${ring(24, 26, 12)} ${oval(24, 26, 7.6, 6.8)}` },
      {
        d: `${oval(24, 26, 7.6, 6.8)} ${oval(20.6, 23.6, 1.6, 2)} ${oval(27.4, 23.6, 1.6, 2)} ${oval(24, 27.6, 2.2, 1.6)}`,
      },
    ],
  },
  {
    key: 'animal-yak',
    label: '牦牛',
    category: 'animal',
    shapes: [{ d: oval(23, 27, 14, 8.6) }, { d: oval(23, 19, 6.4, 5.4) }],
    lines: [
      'M16.6 20c-3.6-2.6-5.4-5.6-4-8.6',
      'M29.4 20c3.6-2.6 5.4-5.6 4-8.6',
      'M11 34.4v6',
      'M16.4 35v6',
      'M22.4 35.4v6',
      'M28.4 35v6',
      'M34 34.4v6',
    ],
    lineWidth: 2.2,
  },
  {
    key: 'animal-deer',
    label: '麋鹿',
    category: 'animal',
    shapes: [
      { d: poly([[25, 26], [38.6, 21.4], [42, 44.6], [25, 44.6]]) },
      { d: oval(22, 28, 8.4, 6.8) },
      { d: oval(14.6, 30, 4.6, 3.2) },
      { d: oval(24.6, 21.6, 2.4, 4.6) },
    ],
    lines: [
      'M23 22c-1.4-5.4-.4-8.6 2.6-11',
      'M28 11.4c2.4-1.4 4.6-.6 5.6 1.6',
      'M26 16c-2.6-1.6-5.4-1.2-6.6 1',
      'M30 26v18',
    ],
    lineWidth: 2.2,
  },

  // ── 美食 ─────────────────────────────────────────────
  {
    key: 'food-hotpot',
    label: '火锅',
    category: 'food',
    shapes: [{ d: 'M12 20h24l-3 13a5 5 0 0 1-5 4H20a5 5 0 0 1-5-4z' }],
    lines: [
      'M12 23c-3.6 0-5 2-5 4.6s1.4 4.6 5 4.6',
      'M36 23c3.6 0 5 2 5 4.6s-1.4 4.6-5 4.6',
      'M17 16.5c0-2.5 3-2.5 3-5s-3-2.5-3-5',
      'M24 15.5c0-2.5 3-2.5 3-5s-3-2.5-3-5',
      'M31 16.5c0-2.5 3-2.5 3-5s-3-2.5-3-5',
    ],
  },
  {
    key: 'food-dimsum',
    label: '早茶点心',
    category: 'food',
    shapes: [
      { d: ring(19, 17, 3.4) },
      { d: ring(24, 15.6, 3.6) },
      { d: ring(29, 17, 3.4) },
      { d: box(10, 21, 28, 4) },
      { d: poly([[13, 25], [35, 25], [33, 38], [15, 38]]) },
    ],
    lines: ['M14 30h20', 'M14.6 34h18.8'],
    lineWidth: 2,
  },
  {
    key: 'food-xiaolongbao',
    label: '小笼包',
    category: 'food',
    shapes: [{ d: 'M24 34.4c-8 0-13-5-13-10.8 0-6 5.8-10 13-10s13 4 13 10c0 5.8-5 10.8-13 10.8z' }],
    lines: [
      'M18 8.4c0-2 3-2 3-4s-3-2-3-3.4',
      'M24 13.6v6.2',
      'M24 19.8l-5.6 4.6',
      'M24 19.8l5.6 4.6',
      'M24 19.8l-8.4 2.6',
      'M24 19.8l8.4 2.6',
    ],
    lineWidth: 2,
  },
  {
    key: 'food-roast-duck',
    label: '烤鸭',
    category: 'food',
    shapes: [
      { d: 'M13 31c-3.4-4.6-1.6-10.6 3.4-13.4 5-2.8 11-1 14 3.6l3.4-2.6 1 5-3 1.4c1.6 4.4-.4 8.6-4.6 10.6z' },
      { d: ring(34, 15.6, 3.6) },
      { d: poly([[37.2, 14], [44.6, 15.4], [37.2, 17]]) },
      { d: oval(24, 41, 16, 2.6) },
    ],
  },
  {
    key: 'food-lamian',
    label: '拉面',
    category: 'food',
    shapes: [{ d: 'M8 24h32c0 9-7 15-16 15S8 33 8 24z' }],
    lines: [
      'M17.6 22c0-4 4-4.4 4-8.4',
      'M24 21c0-4.6 4-4.6 4-9',
      'M30.4 22c0-4 4-4.4 4-8.4',
      'M14 29.4h20',
    ],
  },
  {
    key: 'food-skewer',
    label: '羊肉串',
    category: 'food',
    shapes: [
      { d: `${box(15, 10.5, 10, 7.2)} ${box(15, 20, 10, 7.2)} ${box(15, 29.5, 10, 7.2)}` },
      { d: `${box(28, 14.4, 9.6, 7)} ${box(28, 23.6, 9.6, 7)} ${box(28, 32.8, 9.6, 7)}` },
    ],
    lines: ['M20 4.6v39', 'M33 8.6v35'],
  },
  {
    key: 'food-rice-noodle',
    label: '米粉',
    category: 'food',
    shapes: [{ d: 'M9 26h30c0 8.6-6.6 14-15 14S9 34.6 9 26z' }],
    lines: ['M20 6l4 22', 'M28 6l-4 22', 'M13 30h22', 'M14.6 34.4h18.8'],
  },
  {
    key: 'food-tea',
    label: '茶',
    category: 'food',
    shapes: [
      { d: 'M12 23h20v6.6c0 4.6-4.4 7.4-10 7.4s-10-2.8-10-7.4z' },
      { d: box(7, 38, 30, 3) },
      { d: 'M19.6 18.8c3-3.4 6-3.4 7.6 0-2.6 3.6-5.2 3.6-7.6 0z' },
    ],
    lines: ['M32 26c4 0 5 2 5 4.4s-1 4.4-5 4.4', 'M22 14c0-2.4 3-2.4 3-4.8'],
    lineWidth: 2.2,
  },
  {
    key: 'food-mooncake',
    label: '月饼',
    category: 'food',
    shapes: [
      { d: `${ring(24, 25, 13.6)} ${box(19.6, 20.6, 8.8, 8.8)}` },
      { d: ring(24, 11.4, 2.8), repeat: { count: 8 } },
    ],
  },
  {
    key: 'food-dumpling',
    label: '饺子',
    category: 'food',
    shapes: [
      { d: 'M7 34c0-8.4 4.6-14 11-14s11 5.6 11 14z' },
      { d: 'M21 34c0-8.4 4.6-14 11-14s11 5.6 11 14z' },
    ],
    lines: ['M4 37h40', 'M13 23l-2.6 9', 'M17.6 22.4l-2 9.6', 'M27 23l-2.6 9', 'M31.6 22.4l-2 9.6'],
    lineWidth: 2.2,
  },

  // ── 建筑 ─────────────────────────────────────────────
  {
    key: 'architecture-great-wall',
    label: '长城',
    category: 'architecture',
    shapes: [
      { d: `${box(17, 16, 14, 11)} ${box(21, 21, 6, 6)}` },
      { d: poly([[14, 16], [24, 9.5], [34, 16]]) },
      { d: box(5, 31, 38, 9) },
      { d: `${box(6, 27, 4, 4)} ${box(13, 27, 4, 4)} ${box(20, 27, 4, 4)} ${box(27, 27, 4, 4)} ${box(34, 27, 4, 4)}` },
    ],
  },
  {
    key: 'architecture-tiananmen',
    label: '天安门',
    category: 'architecture',
    shapes: [
      { d: box(6, 37, 36, 7) },
      { d: `${box(12, 22, 24, 15)} ${arch(20, 37, 6, 8)} ${arch(28, 37, 6, 8)} ${arch(24, 34, 4, 6)}` },
      { d: poly([[8, 22], [14, 15], [34, 15], [40, 22]]) },
      { d: poly([[13, 15], [17, 8], [31, 8], [35, 15]]) },
    ],
  },
  {
    key: 'architecture-oriental-pearl',
    label: '东方明珠',
    category: 'architecture',
    shapes: [
      { d: box(23, 2.5, 2, 7) },
      { d: ring(24, 13, 3.4) },
      { d: box(22.6, 15, 2.8, 20) },
      { d: ring(24, 23.5, 6) },
      { d: ring(24, 33, 4) },
      { d: poly([[12, 44.5], [21, 33], [27, 33], [36, 44.5]]) },
    ],
  },
  {
    key: 'architecture-tulou',
    label: '土楼',
    category: 'architecture',
    shapes: [{ d: `${ring(24, 26, 17)} ${ring(24, 26, 8.6)}` }],
    lines: [
      'M24 13.6v6',
      'M24 32.4v6',
      'M13.6 26h6',
      'M32.4 26h6',
      'M17 19l4.4 4.4',
      'M31 19l-4.4 4.4',
      'M17 33l4.4-4.4',
      'M31 33l-4.4-4.4',
    ],
    lineWidth: 2,
  },
  {
    key: 'architecture-palace',
    label: '故宫',
    category: 'architecture',
    shapes: [
      { d: box(6, 38, 36, 6) },
      { d: `${box(13, 24, 22, 14)} ${box(19, 31, 10, 7)}` },
      { d: 'M4 24c4-5 9-7 20-7s16 2 20 7z' },
      { d: poly([[4, 24], [1.2, 20.4], [4.8, 19.4]]) },
      { d: poly([[44, 24], [46.8, 20.4], [43.2, 19.4]]) },
    ],
  },
  {
    key: 'architecture-pagoda',
    label: '古塔',
    category: 'architecture',
    shapes: [
      { d: box(9, 33, 30, 8) },
      { d: box(13, 23, 22, 10) },
      { d: box(23, 12, 4, 11) },
      { d: ring(24, 9, 2.6) },
      { d: 'M4 33c5-4 10-5.6 20-5.6s15 1.6 20 5.6z' },
      { d: 'M8 23c4.4-3.4 9-4.6 16-4.6s11.6 1.2 16 4.6z' },
    ],
  },
  {
    key: 'architecture-arcade',
    label: '骑楼',
    category: 'architecture',
    shapes: [
      {
        d: `${box(5, 20, 38, 22)} ${arch(15, 42, 8, 11)} ${arch(24, 42, 8, 11)} ${arch(33, 42, 8, 11)} ${box(11, 25.4, 4.6, 4.6)} ${box(21.6, 25.4, 4.6, 4.6)} ${box(32.2, 25.4, 4.6, 4.6)}`,
      },
      { d: box(3, 16, 42, 4) },
    ],
  },
  {
    key: 'architecture-cave-house',
    label: '窑洞',
    category: 'architecture',
    shapes: [
      {
        d: `M3 42c0-14.6 9.4-23 21-23s21 8.4 21 23z ${arch(17, 42, 8, 11)} ${arch(31, 42, 8, 11)} ${box(20.4, 28.6, 7.2, 4.4)}`,
      },
    ],
  },
  {
    key: 'architecture-bridge',
    label: '古桥',
    category: 'architecture',
    shapes: [
      { d: box(4, 24, 40, 4.6) },
      { d: box(9, 28.6, 5, 13.4) },
      { d: box(34, 28.6, 5, 13.4) },
      {
        d: `${box(6, 19.6, 3, 4.4)} ${box(13, 19.6, 3, 4.4)} ${box(20, 19.6, 3, 4.4)} ${box(27, 19.6, 3, 4.4)} ${box(34, 19.6, 3, 4.4)} ${box(39, 19.6, 3, 4.4)}`,
      },
    ],
  },
  {
    key: 'architecture-temple',
    label: '寺庙',
    category: 'architecture',
    shapes: [
      { d: `${box(11, 24, 26, 17)} ${arch(20, 41, 6, 8)} ${arch(28, 41, 6, 8)}` },
      { d: 'M5 24c5-5.4 10-7.6 19-7.6s14 2.2 19 7.6z' },
      { d: poly([[5, 24], [1.4, 20], [4.4, 18.6]]) },
      { d: poly([[43, 24], [46.6, 20], [43.6, 18.6]]) },
      { d: box(5, 41, 38, 5) },
    ],
  },

  // ── 植物（含市花） ────────────────────────────────────
  {
    key: 'plant-plum-blossom',
    label: '梅花',
    category: 'plant',
    shapes: [
      { d: ring(24, 15.4, 5.4), repeat: FIVE },
      { d: ring(24, 24, 2.8) },
    ],
    lines: ['M24 21.2v-3.6', 'M21.2 24h-3.6', 'M26.8 24h3.6', 'M24 26.8v3.6'],
    lineWidth: 2,
  },
  {
    key: 'plant-peony',
    label: '牡丹',
    category: 'plant',
    shapes: [
      { d: oval(24, 13, 5.6, 7.6), repeat: { count: 8 } },
      { d: oval(24, 17, 4.4, 5.6), repeat: FIVE },
      { d: ring(24, 24, 3.4) },
    ],
  },
  {
    key: 'plant-osmanthus',
    label: '桂花',
    category: 'plant',
    shapes: [
      { d: `${ring(16, 20, 2.2)} ${ring(20.4, 16.8, 1.8)} ${ring(12, 16.8, 1.8)} ${ring(16, 15.4, 1.8)}` },
      { d: `${ring(32, 26, 2.2)} ${ring(36.4, 22.8, 1.8)} ${ring(28, 22.8, 1.8)} ${ring(32, 21.4, 1.8)}` },
      { d: oval(40, 33, 5.6, 2.6) },
    ],
    lines: ['M6 42c6-6 8.6-13 9.6-20', 'M31.6 30c1-4 2.6-6.6 5-8.6'],
    lineWidth: 2,
  },
  {
    key: 'plant-lotus',
    label: '荷花',
    category: 'plant',
    shapes: [{ d: petal(19, 4.6), repeat: SIX }, { d: oval(24, 24, 3.6, 2.8) }],
    lines: ['M4 40c6-3 12-3 18 0s12 3 18 0'],
  },
  {
    key: 'plant-chrysanthemum',
    label: '菊花',
    category: 'plant',
    shapes: [{ d: petal(12, 2.6), repeat: { count: 16 } }, { d: ring(24, 24, 3.4) }],
  },
  {
    key: 'plant-rose',
    label: '月季',
    category: 'plant',
    shapes: [
      { d: oval(24, 15, 6.4, 8.4), repeat: FIVE },
      { d: oval(14, 36, 6.6, 3), repeat: { count: 2, cx: 24, cy: 34 } },
    ],
    lines: ['M22.6 18.6a4.8 4.8 0 1 1 4.6 7.2 2.6 2.6 0 1 1-3.6-2.6'],
    lineWidth: 2.2,
  },
  {
    key: 'plant-kapok',
    label: '木棉',
    category: 'plant',
    shapes: [{ d: oval(24, 14.4, 7.4, 9.2), repeat: FIVE }, { d: ring(24, 24, 3.2) }],
    lines: ['M24 20.4v8', 'M24 25.6l-5.4 6.4', 'M24 25.6l5.4 6.4', 'M24 25.6l-8 3', 'M24 25.6l8 3'],
    lineWidth: 2,
  },
  {
    key: 'plant-magnolia',
    label: '白玉兰',
    category: 'plant',
    shapes: [{ d: petal(18, 3.6), repeat: SIX }, { d: ring(24, 24, 2.8) }],
  },
  {
    key: 'plant-orchid',
    label: '兰花',
    category: 'plant',
    shapes: [
      { d: oval(24, 14, 3.4, 5.4) },
      { d: oval(16.6, 20, 5.6, 3.2) },
      { d: oval(31.4, 20, 5.6, 3.2) },
      { d: oval(24, 24.6, 4.4, 3.4) },
    ],
    lines: ['M24 28v14', 'M24 34c-5 1.6-9 1-12-2', 'M24 37c5 1.6 9 1 12-2'],
    lineWidth: 2.2,
  },
  {
    key: 'plant-bamboo',
    label: '竹',
    category: 'plant',
    shapes: [
      { d: box(18, 6, 3.4, 12) },
      { d: box(18, 20, 3.4, 12) },
      { d: box(18, 34, 3.4, 10) },
      { d: box(27, 14, 3, 10) },
      { d: box(27, 26, 3, 10) },
      { d: oval(11, 15, 6.4, 2.2) },
      { d: oval(37, 24, 6.4, 2.2) },
    ],
    lines: ['M19.6 18.6h.2', 'M19.6 32.6h.2'],
    lineWidth: 2,
  },
  {
    key: 'plant-azalea',
    label: '杜鹃',
    category: 'plant',
    shapes: [
      { d: petal(11, 4.6), repeat: FIVE },
      { d: oval(24, 24, 2.8, 2.2) },
      { d: oval(12, 34, 6, 3) },
      { d: oval(36, 34, 6, 3) },
    ],
    lines: ['M24 21.2v-8', 'M24 13.2l-2.6-2'],
    lineWidth: 2,
  },
  {
    key: 'plant-camellia',
    label: '山茶花',
    category: 'plant',
    shapes: [
      { d: oval(24, 16, 7, 8), repeat: { count: 7 } },
      { d: ring(24, 24, 3) },
      { d: oval(13, 39, 7.4, 3.4) },
      { d: oval(35, 39, 7.4, 3.4) },
    ],
  },
  {
    key: 'plant-bougainvillea',
    label: '三角梅',
    category: 'plant',
    shapes: [
      { d: poly([[24, 9], [37, 28], [11, 28]]), repeat: { count: 3 } },
      { d: ring(24, 24, 2.6) },
    ],
    lines: ['M24 27.4v14'],
    lineWidth: 2,
  },
  {
    key: 'plant-lilac',
    label: '丁香',
    category: 'plant',
    shapes: [
      {
        d: `M24 6c5.6 6 8.8 12.4 8.8 19.4 0 6.2-3.8 10.4-8.8 10.4s-8.8-4.2-8.8-10.4c0-7 3.2-13.4 8.8-19.4z ${ring(19, 20, 1.8)} ${ring(29, 20, 1.8)} ${ring(24, 14.6, 1.8)} ${ring(19, 28.4, 1.8)} ${ring(29, 28.4, 1.8)}`,
      },
      { d: oval(12.6, 39, 6, 3) },
      { d: oval(35.4, 39, 6, 3) },
    ],
    lines: ['M24 36v8'],
    lineWidth: 2,
  },
  {
    key: 'plant-jasmine',
    label: '茉莉',
    category: 'plant',
    shapes: [
      { d: petal(13, 2.8), repeat: { count: 7 } },
      { d: ring(24, 24, 2.4) },
      { d: ring(39, 14, 2.6) },
      { d: ring(9, 34, 2.6) },
    ],
    lines: ['M37 17c-3 3-5 7-6 12', 'M11 31c1 4 3 7 6 9'],
    lineWidth: 2,
  },
  {
    key: 'plant-hibiscus',
    label: '木芙蓉',
    category: 'plant',
    shapes: [{ d: petal(17, 6.4), repeat: FIVE }, { d: ring(24, 24, 2.8) }],
    lines: ['M24 26v13', 'M24 32h-3', 'M24 35h3'],
    lineWidth: 2.2,
  },
  {
    key: 'plant-pomegranate-flower',
    label: '石榴花',
    category: 'plant',
    shapes: [
      { d: poly([[15, 22], [33, 22], [30, 41], [18, 41]]) },
      { d: petal(12, 4.4), repeat: FIVE },
    ],
    lines: ['M24 41v3.4'],
    lineWidth: 2.2,
  },
];

const BY_KEY = new Map(TRAVEL_ICONS.map((icon) => [icon.key, icon]));

/** 按 key 取图标；未知/空返回 undefined（渲染时回退） */
export function travelIcon(key: string | null | undefined): TravelIcon | undefined {
  return key ? BY_KEY.get(key.trim()) : undefined;
}

/** 把图标渲染成内联 SVG 标记（唯一渲染入口：组件与地图标记共用） */
export function travelIconMarkup(icon: TravelIcon, size: number): string {
  const stroke = icon.lineWidth ?? 2.6;
  const shapes = icon.shapes
    .flatMap((shape) => {
      const count = shape.repeat?.count ?? 1;
      const cx = shape.repeat?.cx ?? 24;
      const cy = shape.repeat?.cy ?? 24;
      return Array.from({ length: count }, (_, index) =>
        count > 1
          ? `<path d="${shape.d}" fill="currentColor" fill-rule="evenodd" transform="rotate(${r2((360 / count) * index)} ${cx} ${cy})"/>`
          : `<path d="${shape.d}" fill="currentColor" fill-rule="evenodd"/>`,
      );
    })
    .join('');
  const lines = (icon.lines ?? [])
    .map(
      (d) =>
        `<path d="${d}" fill="none" stroke="currentColor" stroke-width="${stroke}" stroke-linecap="round" stroke-linejoin="round"/>`,
    )
    .join('');
  return `<svg viewBox="0 0 48 48" width="${size}" height="${size}" style="display:block" aria-hidden="true" focusable="false">${shapes}${lines}</svg>`;
}

/**
 * 地区特色映射（MVP 精选，约 36 条热门城市；其余城市回退默认标记）。
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

/** 解析图标所需的记录字段（Travel record 的 icon 快照 + city 快照） */
export interface TravelIconSource {
  iconKey?: string | null;
  city?: string | null;
}

/** 解析一条记录最终应显示的图标 key：显式选择 > 地区特色 > 无（ADR-0016） */
export function resolveIconKey(record: TravelIconSource): string | undefined {
  return travelIcon(record.iconKey)?.key ?? regionalIconKey(record.city);
}

/** 一条记录最终应显示的图标（显式 > 地区 > 无） */
export function iconForRecord(record: TravelIconSource): TravelIcon | undefined {
  return travelIcon(resolveIconKey(record));
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
