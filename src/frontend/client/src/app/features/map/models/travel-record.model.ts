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
