/**
 * 用户视图模型，对应后端 UserDto。
 * 预留 phoneNumber/avatarUrl/isActive 等字段以匹配后端领域扩展。
 */
export interface User {
  id: number;
  name: string;
  email: string;
  phoneNumber?: string;
  avatarUrl?: string;
  isActive?: boolean;
}

/** 创建用户请求体，对应后端 CreateUserDto */
export interface CreateUserDto {
  name: string;
  email: string;
}

/** 更新用户请求体，对应后端 UpdateUserDto */
export interface UpdateUserDto {
  name: string;
  email: string;
  phoneNumber?: string;
  avatarUrl?: string;
  isActive: boolean;
}

/** 分页结果，对应后端 PagedResult<T> */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
