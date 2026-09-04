/**
 * 开发环境配置（默认）。
 * 生产构建时由 angular.json 的 fileReplacements 替换为 environment.prod.ts。
 */
export const environment = {
  production: false,
  /** 后端 API 基础地址：本地直连 user 微服务 */
  apiBaseUrl: 'http://localhost:8080/api',
  /** travel-history 微服务 API 基础地址（compose 暴露宿主端口 8081） */
  travelApiBaseUrl: 'http://localhost:8081/api',
  /** 高德地图 JS API 2.0 密钥。申请步骤见 README「地图密钥」；
   *  未配置时地图页会提示而非崩溃。填入后无需重启即可生效（刷新页面）。 */
  amap: {
    /** Web 端(JS API) 的 key */
    key: '',
    /** 配套安全密钥 securityJsCode */
    securityJsCode: '',
  },
  appName: 'Travel Map',
};
