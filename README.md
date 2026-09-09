# TravelHistoryMap
collect the location of travel

---

## 启动开发环境

### 1) 准备环境变量

```bash
# Windows CMD
copy .env.example .env
# macOS / Linux / PowerShell
cp .env.example .env
```

编辑 `.env`，设置以下变量（`docker-compose.dev.yml` 用 `${VAR:?VAR is required}` 强制校验，缺失会直接报错）：

| 变量 | 说明 |
|------|------|
| `DB_PASSWORD` | PostgreSQL 密码。注意 `./database/postgres` 数据目录被 Compose 与 k8s PV 共用，改动会影响两边 |
| `JWT_KEY` | JWT 签名密钥，user-service 与 travel-history 共用同一组（签发 / 校验） |
| `DEMO_USER_PASSWORD` | 开发演示账号 `demo@travel.local` 的密码（user-service 以 Development 启动时幂等创建） |

`.env`、Kubernetes Secret 和前端运行时配置均不提交到 Git。ASP.NET 配置键保持不变，Compose 通过环境变量覆盖连接串和 JWT 配置。

### 2) 首次启动（一次性）

```bash
docker compose -f docker-compose.dev.yml up -d
```

等待 Postgres 健康检查通过后，user-service / travel-history 会自动建库建表并写入共享 schema 与种子数据。启动后：

| 服务 | URL |
|------|-----|
| 前端 client | http://localhost:4200 |
| user-service | http://localhost:8080/swagger |
| travel-history | http://localhost:8081/swagger |
| Grafana（可观测性） | http://localhost:3000 |

> `up -d` 只是把服务跑起来，**不会**把工作区改动实时同步进容器。client / server / travel-server 没有挂 volume，容器里跑的是**构建镜像那一刻的代码快照**；想改完源码即时生效请用下面的 watch 模式。

### 3) 开发模式：`docker compose watch`（推荐，源码热同步）

client、server（user-service）、travel-server 三个服务在 `docker-compose.dev.yml` 里配置了 `develop.watch` 规则。运行 `docker compose watch` 后，**改动源码保存即自动同步进容器**，由容器内的 `ng serve` / `dotnet watch` 重新编译并热重载，无需手动重建镜像或重启容器。

> `watch` 本身**不会**创建 / 启动容器，只负责监听与同步。请先 `up -d` 把服务启动起来：

```bash
# ① 首次：先启动全部服务（只需一次）
docker compose -f docker-compose.dev.yml up -d

# ② 开发：保持前台运行 watch（阻塞命令，不要加 -d；退出按 Ctrl+C）
docker compose -f docker-compose.dev.yml watch
```

`watch` 生效规则（对应 `docker-compose.dev.yml` 中各服务的 `develop.watch`）：

| 改动的文件 | watch 动作 | 效果 |
|------------|-----------|------|
| `src/frontend/client/**` 源码 / 静态文件 | `sync` → 容器 `/app`（忽略 `node_modules` / `dist` / `.angular`） | Angular dev server 热重载（http://localhost:4200） |
| `src/backend/**` 源码（含 `shared/`、两个 service） | `sync` → 容器 `/src`（忽略 `bin` / `obj`） | `dotnet watch` 自动重新编译并重启 |
| `src/frontend/client/package.json` / `package-lock.json` | `rebuild` | 重新构建 client 镜像后再同步 |
| `src/backend/services/*/Dockerfile.dev` | `rebuild` | 重新构建对应后端镜像后再同步 |

常用变体：

```bash
# 只想重建并重启某一个服务（例如改了依赖后）
docker compose -f docker-compose.dev.yml up -d --build client

# 停掉整个开发环境
docker compose -f docker-compose.dev.yml down
```

> 提示：
> - `docker compose watch` 是前台阻塞命令，建议单独开一个终端运行。
> - 需要 Docker Compose v2.22.0+。client 已设 `CHOKIDAR_USEPOLLING=true`、后端已设 `DOTNET_USE_POLLING_FILE_WATCHER=true`，在同步 / 挂载文件系统上也能可靠检测变更。
> - 若容器里仍是旧代码快照（例如页面还是旧 UI、或前端 `runtime-config.js` 还是旧高德 key），重跑一次 `docker compose -f docker-compose.dev.yml up -d --build <service>`（或进入 watch 模式）即可让容器与工作区一致。
> - 端口冲突：`client` 固定占用 `4200:4200`，与下方「前端启动」的本地 `npm start` 互斥，二者只能择一。

---

## 账号与登录

应用**只有登录用户才能进入主界面**：`/login`、`/register` 为公开页，其余路由由 `authGuard` 保护，业务请求自动携带 JWT（见 `docs/adr/0005`）。

- **注册**：注册是创建账号的唯一途径，注册成功后跳转登录页；登录时需要输入图片验证码。
- **开发种子账号**（`user-service` 以 Development 启动时幂等创建）：
  - 邮箱：`demo@travel.local`
   - 密码：由 `.env` 中的 `DEMO_USER_PASSWORD` 提供
- 登录后右上角显示用户名与头像（头像可填图片 URL，留空则显示姓名首字母），点击后出现菜单，可进入「我的资料」（`/profile`，修改姓名/邮箱/电话/头像与登录密码）或「登出」。

---

## 前端地图（AMap JS API）密钥

地图页（`/map`，Angular 前端 `src/frontend/client`）使用高德地图 JS API 2.0 渲染用户的旅行足迹（决策见 `docs/adr/0003`、`docs/adr/0004`）。高德 SDK 需要浏览器端密钥才能加载：

1. 打开 https://lbs.amap.com ，注册/登录并完成个人实名认证。
2. 控制台 → 应用管理 → 我的应用 → **创建新应用** → 添加 key：服务平台选 **“Web端(JS API)”**，类型 “JavaScript API”。
3. 在 key 的**域名白名单**中加入本页地址（本地开发为 `http://localhost:4200`；生产为部署域名）。
4. 复制 `src/frontend/client/public/runtime-config.example.js` 为 `runtime-config.js`，再把 key 与配套的 **securityJsCode** 填入其中。该文件已被 Git 忽略。

未配置密钥时地图页会显示指引而不是崩溃；填入后刷新页面即可。

CI/CD 发布时，在 GitHub Actions Secrets 中配置 `AMAP_KEY` 和 `AMAP_SECURITY_JS_CODE`，工作流会在构建前生成运行时配置文件。

Kubernetes 部署前请使用部署平台的 Secret 管理功能创建 `app-secret`，字段模板见 `infra/k8s/base/secret.example.yaml`；不要把真实的 `secret.yaml` 放回仓库。

前端启动：`cd src/frontend/client && npm install && npm start`（默认 http://localhost:4200）。
后端 dev 端口：user-service `http://localhost:8080`，travel-history `http://localhost:8081`（由 docker-compose.dev.yml 暴露）。

---

## 可观测性 (OpenTelemetry)

```
user-service (.NET OTel SDK)
    │  OTLP/gRPC :4317
    ▼
┌───────────────────────────┐
│  OTel Collector            │
│  receivers:  otlp         │
│  processors: batch,       │
│    memory_limiter          │
│  pipelines:               │
│    traces  → Jaeger        │
│    metrics → Prometheus    │
│    logs    → Loki          │
│    debug   → Console       │
└──┬────────┬───────────────┘
   │        │            │
   ▼        ▼            ▼
┌──────┐ ┌──────────┐ ┌──────┐
│Jaeger│ │Prometheus│ │ Loki │
└──────┘ └────┬─────┘ └──┬───┘
              │           │
              ▼           ▼
         ┌─────────────────────┐
         │       Grafana        │
         │  数据源: Prometheus  │
         │         + Loki       │
         └─────────────────────┘
```

### 访问地址

| 服务 | URL | 说明 |
|------|-----|------|
| **Swagger UI** | http://localhost:8080/swagger | API 文档 + 交互测试 |
| **Jaeger UI** | http://localhost:16686 | 分布式追踪查询 (Traces) |
| **Prometheus** | http://localhost:9090 | 时序指标查询 (Metrics) |
| **Grafana** | http://localhost:3000 | 统一可视化（Traces + Metrics + Logs），admin/admin |
| **`/metrics`** | http://localhost:8080/metrics | Prometheus 抓取端点（应用直接暴露） |
| **`/health`** | http://localhost:8080/health | 健康检查 |
| **Loki API** | http://localhost:3100 | 日志查询 API（Grafana 中以 Loki 为数据源查询） |
| **Collector debug** | `docker logs otel-collector` | Collector 控制台输出 traces + logs 内容 |
| **App logs** | `docker logs dotnet-api-dev` | .NET 控制台日志（DEBUG 构建含 ConsoleExporter） |

### 配置分布

| 配置 | 文件 | 说明 |
|------|------|------|
| 应用侧 Exporter / Processor | `src/backend/shared/Shared.Observability/Telemetry.cs` | OTLP、Prometheus 导出器 + AlwaysOnSampler + Console（DEBUG） |
| Collector pipelines | `infra/observability/otel-collector-config.yaml` | receivers/processors/exporters: traces→Jaeger, logs→Loki, metrics→Prometheus |
| Loki 存储配置 | `infra/observability/loki/loki-config.yaml` | 本地文件存储、OTLP 接收端点、保留策略 |
| 开发环境变量 | `docker-compose.dev.yml` | `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317` |
| 生产环境变量 | `infra/k8s/base/configmap.yaml` | K8s OTLP 端点 `http://otel-collector:4317` |
| Prometheus 抓取配置 | `infra/observability/prometheus/prometheus.yml` | 抓取目标 `server:8080/metrics` |
| Grafana 数据源 | `infra/observability/grafana/datasources/datasource.yml` | Prometheus + Loki 数据源预置 |
| Grafana 仪表板 | `infra/observability/grafana/dashboards/` | ASP.NET Core 指标预置仪表板 |
| 请求日志中间件 | `src/backend/services/user-service/src/User.Api/Program.cs` | 每个 HTTP 请求生成结构化日志 → OTel → Loki |

### 数据流

```
App → Collector (OTLP/gRPC :4317) ─┬─→ Jaeger :4317    (Traces)
                                   ├─→ Loki :3100/otlp  (Logs)
                                   ├─→ Prometheus :8889 (Metrics，备选)
                                   └─→ Console          (debug exporter)
App → /metrics ←── Prometheus scrape :9090
Grafana :3000 ←── Prometheus (Metrics) + Loki (Logs)
```

### 遥测覆盖

- **Traces**: ASP.NET Core HTTP 请求、Npgsql 数据库 SQL 命令（含耗时、SQL 文本）
- **Metrics**: HTTP 请求速率/延迟/活跃连接、GC 堆/回收频率、线程池、CPU、内存
- **Logs**: 请求日志中间件输出结构化日志（Method、Path、StatusCode、ElapsedMs），可通过 Grafana Explore 以 Loki 数据源查询
- **Collector debug exporter**: `docker logs otel-collector` 查看完整 span + log 内容
- **App ConsoleExporter**: DEBUG 构建时 span + log 输出到 `docker logs dotnet-api-dev`

---

## GitHub Actions CI/CD

### 概述

| 项 | 说明 |
|----|------|
| 工作流文件 | `.github/workflows/ci.yml` |
| 触发时机 | push 到 `main`（构建+测试+部署）、创建/更新 PR（仅构建+测试）、手动触发 |
| 运行环境 | GitHub 免费提供的 `ubuntu-latest` 虚拟机 |
| 费用 | 公共仓库免费；镜像存 ghcr.io（public 免费）；Azure 部分落在 Container Apps 月度免费额度内 |
| 用途 | 每次代码变更自动跑：构建 + 单元测试 + Docker 镜像推送（ghcr.io）+ 部署到 Azure Container Apps |

### 架构

```
浏览器 ──► gateway（nginx 网关，唯一 external 入口）
              ├── /api/*  ──► user-service（internal，.NET WebAPI :8080）
              └── /*      ──► client（internal，nginx 静态站 :80）
```

三个容器应用都在 Azure Container Apps（`cae-travelmap` 环境），`min-replicas 0` 空闲缩容到零，落在免费额度内。数据库用 Azure PostgreSQL Flexible Server（B1ms 免费层）。

### 流水线结构

```
push 到 main（或 PR / 手动触发）
   │
   ▼
┌─ docker-build（job ①：构建 + 测试 + 推镜像）─┐
│ checkout → setup-buildx → 登录 GHCR          │
│ → 构建并推送 3 个镜像到 ghcr.io（org 命名空间）│
│     client / user-service / gateway          │
│   （容器内跑 dotnet test / ng test）          │
│ → 私有仓库时把镜像设为 public                 │
└──────────────┬───────────────────────────────┘
               │ needs: docker-build（输出镜像 tag）
               ▼  （仅 push main / 手动触发，PR 跳过）
┌─ deploy（job ②：部署到 Azure Container Apps）┐
│ azure/login（OIDC 无密码登录）                │
│ → container-apps-deploy × 3                  │
│   更新 user-service / client / gateway 镜像  │
└──────────────────────────────────────────────┘
```

任何一步失败 → job 失败 → 整个 workflow 标红。测试失败会直接中断镜像构建，因此一个 job 就完成了"编译 + 单元测试 + 镜像可出"的全部验证；验证通过后自动部署。

### 设计原则：Dockerfile 是"唯一构建真相"

| 验证项 | 在哪里执行 |
|--------|-----------|
| 依赖安装 | Dockerfile build 阶段（`dotnet restore` / `npm ci`） |
| 编译 | Dockerfile build 阶段（`dotnet publish` / `ng build`） |
| 单元测试 | Dockerfile build 阶段（`dotnet test` / `ng test --watch=false`，测试失败即构建失败） |
| 镜像可出 | CI 的 `docker build` |
| 推送 | `docker/build-push-action` 推送到 ghcr.io |

好处：构建逻辑只写一处；**本地 `docker build` 与 CI 结果完全一致**；部署直接拉同一份镜像。
代价：测试日志嵌在镜像构建日志中；首次构建要拉 base 镜像（gha 层缓存后二次运行很快）。

### 任务详解（job ①：docker-build）

| 步骤 | 作用 |
|------|------|
| `actions/checkout@v5` | 把仓库代码克隆到虚拟机（每个 workflow 的第一步） |
| `docker/setup-buildx-action@v4` | 启用 BuildKit 构建器，是 `cache-from/to` 层缓存的前置 |
| `docker/login-action@v3` | 登录 GHCR（用 `GITHUB_TOKEN`，无需 PAT） |
| `docker/build-push-action@v7`（client） | 构建前端镜像：`npm ci` → `ng build` → `ng test` → nginx 托管 |
| `docker/build-push-action@v7`（user-service） | 构建后端镜像：restore → `dotnet test` → publish |
| `docker/build-push-action@v7`（gateway） | 构建网关镜像：nginx + 路由模板（`src/gateway/`） |
| Set package visibility | 仓库为私有自动把镜像设为 public（Container Apps 免凭证拉取） |

镜像构建的关键参数：

| 参数 | 值 | 原因 |
|------|----|------|
| `context` | `./src/backend` / `./src/frontend/client` / `./src/gateway` | 后端必须 `src/backend`：Dockerfile 里 `COPY` 了 `services/` 与 `shared/`，路径相对 context 解析 |
| `file` | 后端需显式指定 Dockerfile 路径 | Dockerfile 不在 context 根 |
| `push` | PR 时 `false`，push main 时 `true` | 只构建验证 vs 推送 + 打 `latest` 标签 |
| `tags` | `:短SHA` + `:latest` | 短 SHA 支持回滚，latest 供手动拉取 |
| `cache-from/to` | `type=gha,scope=frontend/backend/gateway` | 层缓存存 GitHub Actions，二次运行加速；`scope` 区分三个镜像避免缓存冲突 |
| `registry/命名空间` | `ghcr.io/${{ github.repository_owner }}` | 镜像属于仓库所属组织/用户 |

### 任务详解（job ②：deploy）

| 步骤 | 作用 |
|------|------|
| `needs: docker-build` | 声明依赖：测试/构建通过才部署 |
| `if: github.event_name != 'pull_request'` | PR 只验证不部署 |
| `permissions: id-token: write` | OIDC 登录 Azure 需要请求短期 JWT（无密码） |
| `azure/login@v2` | 用 client-id / tenant-id / subscription-id 三个 secret 无密码登录 Azure |
| `Update app images` | 用 `az containerapp update --image` 更新 user-service / client / travel-service / gateway 的镜像（**仅镜像**，保留应用已配置的 env/secrets；运行配置在 ACA 上一次性配置） |

部署所需的三个 secrets（Azure 部署向导创建时自动生成，存在仓库 Settings → Secrets）：

```
AZUREAPPSERVICE_CLIENTID_3E4B653535F64E518FC068312A4ACF78
AZUREAPPSERVICE_TENANTID_A2B2CD6D4C5C4A77B27AE5DA70E292AE
AZUREAPPSERVICE_SUBSCRIPTIONID_12264A5C94164B61945E7CA2E9EEE253
```

### 一次性 Azure 初始化

CI 的 deploy 只更新已存在应用的镜像，**不负责创建**。首次部署前需在 Azure 建好资源（或运行仓库根目录的 `deploy-azure.sh`）：

```bash
PG_PASSWORD="你的密码" bash deploy-azure.sh
```

脚本会创建：资源组 `travelMap`、容器应用环境 `cae-travelmap`（位于 `eastus`）、PostgreSQL Flexible Server（B1ms 免费层）、三个 Container App（client/user-service 为 internal，gateway 为 external 唯一入口）。之后推 `main` 即自动部署。

### 常见坑

- 忘记 `actions/checkout` → 找不到代码
- Docker 构建 context 写错 → `COPY` 找不到 `shared/`（后端 context 必须是 `src/backend`）
- Azure 资源未提前创建 → deploy 报 "not found"（deploy action 只更新镜像，不创建应用）
- 私有仓库镜像未设为 public → Container Apps 拉取需要额外凭证
- OIDC secrets 权限只到 App Service 资源 → deploy 报 403/AuthorizationFailed，需给 service principal 补 Contributor
- `schedule` 是 UTC 时区，注意换算
- secrets 未配置就引用 → `Unrecognized named-value: 'secrets'`
- PostgreSQL 免费层 12 个月到期转付费，注意续期提醒
