/**
 * 生产环境配置。
 * 通过反向代理/网关将 /api 转发到后端各微服务。
 *
 * 注意：travelApiBaseUrl 必须为「可直达 travel-history 的绝对地址」。
 * 现有网关只把 /api 转发给 user-service 且本轮不改造 k8s/Azure（见 docs/adr/0002、0003），
 * 因此生产部署时需要把该值替换为实际网关地址（如 https://your-host/travel-api），
 * 或将来在网关为 travel-history 增加路由后改用相对路径并同步修改拦截器策略。
 */
export const environment = {
  production: true,
  apiBaseUrl: '/api',
  travelApiBaseUrl: 'https://travel-api.example.com/api',
  /** 高德地图 JS API 2.0 密钥（申请步骤见 README「地图密钥」），
   *  记得在控制台把部署域名加进 key 的域名白名单。 */
  amap: {
    key: '',
    securityJsCode: '',
  },
  appName: 'Travel Map',
};
