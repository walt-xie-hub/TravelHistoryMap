/**
 * 旅行记录的展示辅助：本地时区格式化与分组（同地点多次停留按近似坐标合并，见 docs/adr/0004）。
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

/** UTC ISO → 本地 “yyyy-MM-dd HH:mm” */
export function fmtDateTime(iso: string): string {
  const d = new Date(iso);
  return `${fmtDate(iso)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
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

/** 同地点（近似坐标）合并分组；key 用 WGS-84 4 位小数（约 11 米）聚类 */
export interface TravelMarkerGroup {
  key: string;
  /** 组内记录，按 arrivedAt 倒序（入参已有序时保持） */
  records: TravelRecord[];
  /** WGS-84 坐标（渲染到高德前经 wgs84ToGcj02 转换） */
  lng: number;
  lat: number;
}

export function groupRecords(records: readonly TravelRecord[]): TravelMarkerGroup[] {
  const byKey = new Map<string, TravelRecord[]>();
  for (const record of records) {
    const key = `${record.longitude.toFixed(4)},${record.latitude.toFixed(4)}`;
    const bucket = byKey.get(key);
    if (bucket) bucket.push(record);
    else byKey.set(key, [record]);
  }
  return [...byKey.entries()]
    .map(([key, bucket]) => {
      const [lngText, latText] = key.split(',');
      return {
        key,
        records: bucket,
        lng: Number(lngText),
        lat: Number(latText),
      } satisfies TravelMarkerGroup;
    })
    // 组间按组内最新到达倒序，保证标注/列表顺序稳定
    .sort((a, b) => b.records[0]!.arrivedAt.localeCompare(a.records[0]!.arrivedAt));
}
