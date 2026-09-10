/**
 * 旅行记录的展示辅助：本地时区格式化与地图标记分组
 * （城市优先、无 City 回退近似坐标聚类，见 docs/adr/0017 与 0004）。
 */
import type { TravelRecord } from '../models/travel-record.model';

function pad(n: number): string {
  return String(n).padStart(2, '0');
}

/** UTC ISO → 本地 “yyyy-MM-dd” */
export function fmtDate(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Date → datetime-local 输入框所需的本地 “yyyy-MM-ddTHH:mm” 值（快捷“再来一次/结束停留”默认值用） */
export function toDatetimeLocal(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** UTC ISO → 本地友好日期时间：同年省略年份，如 “9月6日 16:00”，跨年带年份 */
export function fmtDateTime(iso: string): string {
  const d = new Date(iso);
  const date = d.getFullYear() === new Date().getFullYear()
    ? `${d.getMonth() + 1}月${d.getDate()}日`
    : `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日`;
  return `${date} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** 一段停留的展示区间文案：同一天显示时刻，跨天显示日期 */
export function fmtRange(record: TravelRecord): string {
  const arrived = fmtDateTime(record.arrivedAt);
  if (!record.departedAt) return `${arrived} · 至今`;
  const departed = fmtDateTime(record.departedAt);
  const sameDay = fmtDate(record.arrivedAt) === fmtDate(record.departedAt);
  return sameDay ? `${arrived} ~ ${fmtTime(record.departedAt)}` : `${arrived} ~ ${departed}`;
}

function fmtTime(iso: string): string {
  const d = new Date(iso);
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** 停留持续时长（进行中按距今计算），如 “2 天 3 小时”；不足 1 分钟显示 “不足 1 分钟” */
export function fmtDuration(record: TravelRecord): string {
  const end = record.departedAt ? new Date(record.departedAt).getTime() : Date.now();
  const ms = Math.max(0, end - new Date(record.arrivedAt).getTime());
  const minutes = Math.floor(ms / 60_000);
  if (minutes < 1) return '不足 1 分钟';
  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);
  const mins = minutes % 60;
  if (days > 0) return mins > 0 ? `${days} 天 ${hours} 小时` : `${days} 天`;
  if (hours > 0) return mins > 0 ? `${hours} 小时 ${mins} 分` : `${hours} 小时`;
  return `${mins} 分钟`;
}

/**
 * 地图标记分组（ADR-0017）：
 * - 有 City 快照的记录 → 按城市合成**城市标记**（一个城市只出一个标记）；
 * - 无 City 的旧记录 → 保留原行为，按 WGS-84 4 位小数（≈11 米）合成**地点标记**（ADR-0004）。
 */
export type MapMarkerKind = 'city' | 'place';

export interface MapMarkerGroup {
  kind: MapMarkerKind;
  /** 稳定标识：`city:上海` / `place:31.2304,121.4737` */
  key: string;
  /** 城市标记的城市名（地点标记为 undefined） */
  city?: string;
  /** 组内记录（保持入参顺序；入参按到达时间倒序） */
  records: TravelRecord[];
  /** 标记锚点（WGS-84）：城市标记取该城所有记录的**质心**，地点标记取该坐标 */
  lng: number;
  lat: number;
}

export function groupMapMarkers(records: readonly TravelRecord[]): MapMarkerGroup[] {
  const byKey = new Map<string, { kind: MapMarkerKind; city?: string; bucket: TravelRecord[] }>();
  for (const record of records) {
    const city = record.city?.trim();
    const kind: MapMarkerKind = city ? 'city' : 'place';
    const key = city
      ? `city:${city}`
      : `place:${record.longitude.toFixed(4)},${record.latitude.toFixed(4)}`;
    const existing = byKey.get(key);
    if (existing) existing.bucket.push(record);
    else byKey.set(key, { kind, city: city || undefined, bucket: [record] });
  }

  return [...byKey.entries()]
    .map(([key, entry]) => {
      const { kind, city } = entry;
      // 组内按到达时间倒序（不依赖入参顺序）：信息窗「最新一条在前」，也决定该组的排序键
      const bucket = [...entry.bucket].sort((a, b) => b.arrivedAt.localeCompare(a.arrivedAt));
      if (kind === 'city') {
        const lng = bucket.reduce((sum, r) => sum + r.longitude, 0) / bucket.length;
        const lat = bucket.reduce((sum, r) => sum + r.latitude, 0) / bucket.length;
        return { kind, key, city, records: bucket, lng, lat } satisfies MapMarkerGroup;
      }
      const [lngText, latText] = key.slice('place:'.length).split(',');
      return {
        kind,
        key,
        records: bucket,
        lng: Number(lngText),
        lat: Number(latText),
      } satisfies MapMarkerGroup;
    })
    // 组间按组内最新到达倒序，保证标注/列表顺序稳定
    .sort((a, b) => b.records[0]!.arrivedAt.localeCompare(a.records[0]!.arrivedAt));
}
