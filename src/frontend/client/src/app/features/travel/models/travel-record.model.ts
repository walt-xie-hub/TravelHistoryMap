/**
 * travel-history 视图模型，对应后端 TravelRecordDto / PagedResult<T>（见 travel-history 的
 * Travel.Api + Travel.Application/DTOs）。时间序列化为 ISO 8601 UTC 字符串；departedAt 为空 = 停留进行中。
 */

export interface TravelRecord {
  id: number;
  userId: number;
  /** 地点名称快照（如 “上海”） */
  locationName: string;
  /** 纬度快照（WGS-84，-90 ~ 90） */
  latitude: number;
  /** 经度快照（WGS-84，-180 ~ 180） */
  longitude: number;
  /** 到达时间（UTC ISO 8601） */
  arrivedAt: string;
  /** 离开时间（UTC ISO 8601）；null = 仍在当地 */
  departedAt: string | null;
  description: string | null;
  /** 标签（ADR-0014）：自由文本，最多 8 个 */
  tags?: string[];
  /** 精选收藏（ADR-0014） */
  isFavorite?: boolean;
  /** 城市快照（ADR-0015）：地级市短名，可为空 */
  city?: string | null;
  images?: TravelImage[];
}

export interface TravelImage {
  id: number;
  originalFileName: string;
  contentType: string;
  fileSize: number;
  thumbnailUrl: string;
  originalUrl: string;
}

export interface CreateTravelRequest {
  locationName: string;
  latitude: number;
  longitude: number;
  arrivedAt: string;
  departedAt?: string | null;
  description?: string | null;
  /** 标签（ADR-0014） */
  tags?: string[];
  /** 精选收藏（ADR-0014） */
  isFavorite?: boolean;
  /** 城市快照（ADR-0015） */
  city?: string | null;
}

/** “再来一次”预填数据：由触发方经 router state 传给新建页（复用既有地点的名称/坐标/城市） */
export interface TravelCreatePrefill {
  locationName: string;
  longitude: number;
  latitude: number;
  city?: string | null;
  /** 到达时间（可选；缺省=当前时刻） */
  arrivedAt?: string;
}

/** 分页结果，字段与后端 PagedResult<T> 序列化一致（camelCase） */
export interface TravelPagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** 侧栏时间范围筛选的语义化请求；from/to 为 UTC ISO 字符串（到达时间窗） */
export type TravelRangeKind = 'all' | 'last30' | 'year' | 'custom';

export interface TravelRangeRequest {
  kind: TravelRangeKind;
  from?: string;
  to?: string;
}

/** 分享（ADR-0012）：创建返回 token 与相对路径 */
export interface ShareCreated {
  token: string;
  url: string;
}

/** 公开只读快照的单行（地点/时间/已消毒正文；无图片/坐标） */
export interface ShareSnapshotRow {
  locationName: string;
  arrivedAt: string;
  departedAt: string | null;
  description: string | null;
}

export interface PublicShareSnapshot {
  title: string;
  createdAt: string;
  recordCount: number;
  rows: ShareSnapshotRow[];
}

