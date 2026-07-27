/**
 * 通用分页结果模型（跨 feature 复用）。
 */
export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
