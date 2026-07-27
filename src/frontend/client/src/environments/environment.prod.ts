/**
 * 生产环境配置。
 * 通过反向代理/网关将 /api 转发到后端各微服务。
 */
export const environment = {
  production: true,
  apiBaseUrl: '/api',
  appName: 'Travel Map',
};
