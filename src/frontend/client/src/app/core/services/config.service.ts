import { Injectable } from '@angular/core';
import { environment } from '@env/environment';

/**
 * 全局配置服务：集中读取环境配置，避免业务代码直接依赖 environment 文件。
 */
@Injectable({ providedIn: 'root' })
export class ConfigService {
  get production(): boolean {
    return environment.production;
  }

  get apiBaseUrl(): string {
    return environment.apiBaseUrl;
  }

  get appName(): string {
    return environment.appName;
  }
}
