/**
 * 邮戳（ADR-0018）：圆形日戳，压在邮票一角形成"销票"效果。
 *
 * 内容是真实日戳的语义：**地名**（Travel record 的 City 快照优先，无快照用地点名的历史快照；
 * 不为邮戳新增逆地理请求）＋**日期**（默认主记录到达日期，可改）＋中间横条「旅行纪念」。
 *
 * 几何计算（弧线路径、销票角度）是纯函数：文字沿上/下弧用 SVG `<textPath>` 排布，
 * 角度与路径由这里算好，组件只负责渲染。可被 vitest 直接覆盖。
 */

/** 日戳直径（px，1181×1748 画布上的装饰尺寸，约画布宽的 15%） */
export const POSTMARK_SIZE = 180;

/** 销票：邮戳盖在邮票上的旋转角度范围（度）——负角度＝逆时针倾斜，像手盖的 */
const CANCEL_ANGLE_MIN = 6;
const CANCEL_ANGLE_SPREAD = 9;

/** 地名沿上弧（左→右），日期沿下弧（左→右） */
const PLACE_ARC = { start: 202, end: 338 } as const;
const DATE_ARC = { start: 158, end: 22 } as const;

/** 中间横条里的字样：明确这是旅行纪念，不是真实邮政业务戳 */
export const POSTMARK_NOTE = '旅行纪念';

export interface PostmarkGeometry {
  /** 外圈直径 */
  size: number;
  /** 弧线文字所在半径 */
  arcRadius: number;
  /** 内圈（细线）半径 */
  innerRadius: number;
  /** 地名沿上弧的 SVG path（供 <textPath> 使用） */
  placeArc: string;
  /** 日期沿下弧的 SVG path */
  dateArc: string;
  /** 中间横条宽度 */
  noteWidth: number;
}

function polar(radius: number, deg: number): [number, number] {
  const rad = (deg * Math.PI) / 180;
  return [round2(radius * Math.cos(rad)), round2(radius * Math.sin(rad))];
}

function round2(value: number): number {
  return Math.round(value * 100) / 100;
}

/**
 * 圆弧路径：`clockwise` 决定扫掠方向（SVG 坐标 y 向下，角度增大＝顺时针）。
 * 上弧从左到右顺时针扫过顶点，下弧要读起来仍是从左到右、因此反向扫掠。
 */
export function arcPath(
  radius: number,
  startDeg: number,
  endDeg: number,
  clockwise: boolean,
): string {
  const [sx, sy] = polar(radius, startDeg);
  const [ex, ey] = polar(radius, endDeg);
  const largeArc = Math.abs(endDeg - startDeg) > 180 ? 1 : 0;
  return `M ${sx} ${sy} A ${radius} ${radius} 0 ${largeArc} ${clockwise ? 1 : 0} ${ex} ${ey}`;
}

/** 按直径算出日戳的全部几何量（相对圆心，单位 px；调用方用 transform 平移到落点） */
export function postmarkGeometry(size: number = POSTMARK_SIZE): PostmarkGeometry {
  const radius = size / 2;
  return {
    size,
    arcRadius: round2(radius - 11),
    innerRadius: round2(radius - 26),
    placeArc: arcPath(radius - 11, PLACE_ARC.start, PLACE_ARC.end, true),
    dateArc: arcPath(radius - 11, DATE_ARC.start, DATE_ARC.end, false),
    noteWidth: round2(size * 0.56),
  };
}

/**
 * 销票角度：对同一条记录稳定（同一张明信片每次渲染一致），不同记录略有差异（-6° ~ -14°），
 * 看起来像"随手盖上去的"而不是机械居中的图标。
 */
export function stampCancelAngle(seed: number): number {
  const normalized = Math.abs(Math.trunc(seed)) % CANCEL_ANGLE_SPREAD;
  return -(CANCEL_ANGLE_MIN + normalized);
}

/** 邮戳文字：地名 + 日期 + 纪念字样（无障碍文案与测试断言共用同一口径） */
export function postmarkText(place: string, date: string): string {
  return `${place} ${date} ${POSTMARK_NOTE}`;
}

/** 日期文本：日戳习惯写成 2026.09.05 */
export function postmarkDateText(isoDate: string): string {
  return isoDate.replaceAll('-', '.');
}
