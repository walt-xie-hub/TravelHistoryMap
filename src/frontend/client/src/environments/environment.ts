/**
 * 开发环境配置（默认）。
 * 生产构建时由 angular.json 的 fileReplacements 替换为 environment.prod.ts。
 */
export const environment = {
  production: false,
  /** 后端 API 基础地址：本地直连 user 微服务 */
  apiBaseUrl: 'http://localhost:8080/api',
  appName: 'Travel Map',
};
