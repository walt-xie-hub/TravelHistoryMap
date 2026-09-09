/**
 * 地图时光轴（Map timeline）的分组工具（ADR-0015）。
 *
 * City run（停留段）：把按到达时间升序排列的 Travel records 切分——
 * “相邻且 City 快照相同”的一段合成一个 run；中途到过别处再回到同一城市＝新 run。
 * 无 City 的记录各成独立 run（不合并未知城市），节点以地点名作为标签。
 */
import type { TravelRecord } from '../models/travel-record.model';

export interface TimelineRun {
  /** 稳定键：有 City 用城市名，否则用首条记录 id 保证唯一 */
  key: string;
  /** City 快照（可为空） */
  city: string | null;
  /** 节点标签：城市名（无则地点名） */
  label: string;
  /** 组内记录，按到达时间升序 */
  records: TravelRecord[];
}

export function buildCityRuns(records: readonly TravelRecord[]): TimelineRun[] {
  const sorted = [...records].sort((a, b) => a.arrivedAt.localeCompare(b.arrivedAt));
  const runs: TimelineRun[] = [];
  for (const record of sorted) {
    const city = record.city?.trim() || null;
    const last = runs[runs.length - 1];
    if (last && city && last.city === city) {
      last.records.push(record);
      continue;
    }
    runs.push({
      key: city ?? `n-${record.id}`,
      city,
      label: city ?? record.locationName,
      records: [record],
    });
  }
  return runs;
}

/** 该 run 中“最近到达”的一条（节点详情默认打开它，见 ADR-0015）。 */
export function newestOf(run: TimelineRun): TravelRecord {
  return run.records[run.records.length - 1]!;
}
