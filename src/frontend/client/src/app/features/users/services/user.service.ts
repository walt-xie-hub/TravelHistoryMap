import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateUserDto, PagedResult, UpdateUserDto, User } from '../models/user.model';

/**
 * 用户 API 服务：对接后端 user 微服务 `/api/users`。
 * 请求使用相对路径，由 apiBaseUrlInterceptor 统一拼接基础地址。
 */
@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);
  private readonly resource = '/users';

  getPaged(page: number, pageSize: number): Observable<PagedResult<User>> {
    const params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    return this.http.get<PagedResult<User>>(this.resource, { params });
  }

  getById(id: number): Observable<User> {
    return this.http.get<User>(`${this.resource}/${id}`);
  }

  create(payload: CreateUserDto): Observable<User> {
    return this.http.post<User>(this.resource, payload);
  }

  update(id: number, payload: UpdateUserDto): Observable<User> {
    return this.http.put<User>(`${this.resource}/${id}`, payload);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.resource}/${id}`);
  }
}
