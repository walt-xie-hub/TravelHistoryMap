# Map page renders on AMap JSAPI 2.0; stored coordinates stay WGS-84, converted client-side to GCJ-02

地图页（`/map`）选用**高德地图 JS API 2.0**（Web 端 key + `securityJsCode` 安全密钥 + 域名白名单）作为唯一渲染引擎，不用 npm 地图库，进入页面时动态注入官方 script（零依赖加载器）。选高德而非最初设想的百度，出于免费申请成本相当而高德文档与 2D/WebGL 兼容体验更好；坐标系约定随之确定为 GCJ-02。

坐标基准决策：`TravelRecords.latitude / longitude`（Location snapshot）**存储统一为 WGS-84**（与 travel.http 样例数据一致），仅在前端展示时用标准算法转换到高德的 GCJ-02（`wgs84→gcj02`），避免数据 vendor-locked。若将来更换/增加引擎（百度 BD-09、OSM 直用 WGS-84），只需新增对应转换函数并在加载层切换，数据层零改动；从地图取点录入时保存前做逆转换（`gcj02→wgs84`）。

密钥与白名单需在高德控制台人工配置（代理无法代办实名认证），代码通过 `environment.amap.*` 注入占位；未配置时地图页展示可操作的提示而非崩溃。

其余已收敛边界：应用无登录概念，`userId` 由地图页下拉（user-service 数据源）选择并 `localStorage` 记忆；本次只做只读标注 + 右侧停留时间线（按 `arrivedAt` 倒序），不做录入。
