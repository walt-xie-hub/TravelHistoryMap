import { runtimeConfig } from './runtime-config';

/**
 * 生产环境配置。
 * 通过反向代理/网关将 /api 转发到后端各微服务。
 *
 * 浏览器和手机端都通过同一个公共 API host 访问服务；网关按 /api/travels 分流。
 */
export const environment = {
  production: true,
  apiBaseUrl: '/api',
  travelApiBaseUrl: '/api',
  /** 高德地图 JS API 2.0 密钥（申请步骤见 README「地图密钥」），
   *  记得在控制台把部署域名加进 key 的域名白名单。 */
  amap: {
    key: runtimeConfig.amapKey ?? '',
    securityJsCode: runtimeConfig.amapSecurityJsCode ?? '',
  },
  appName: 'Travel Map',
};
