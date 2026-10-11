/**
 * 足迹概览（Footprint overview，ADR-0025）。
 *
 * 与后端 `Shared.Contracts/FootprintOverview` 一一对应。它是**委托调用**的结果：
 * user-service 以当前登录用户的名义向 travel-service 取聚合，浏览器不直接跨服务拼装。
 *
 * 口径：不含回收站；cityCount 只统计有 City 快照的记录（派生城市不落库，ADR-0017）。
 */
export interface FootprintOverview {
  /** 记录数（不含回收站，ADR-0013） */
  totalRecords: number;
  /** 去重后的城市数（仅 City 快照） */
  cityCount: number;
  /** 精选收藏数（ADR-0014） */
  favoriteCount: number;
  /** 最早一次到达时间；无记录为 null */
  firstArrivedAt: string | null;
  /** 最近一次到达时间；无记录为 null */
  lastArrivedAt: string | null;
}
