# TravelHistoryMap
collect the location of travel

---

## 启动开发环境

```bash
docker compose -f docker-compose.dev.yml up -d
```

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

## GitHub Actions CI

### 概述

| 项 | 说明 |
|----|------|
| 工作流文件 | `.github/workflows/ci.yml` |
| 触发时机 | push 到 `main`、创建/更新 PR、手动触发（Actions 页面 "Run workflow"） |
| 运行环境 | GitHub 免费提供的 `ubuntu-latest` 虚拟机 |
| 费用 | 公共仓库免费（2000 分钟/月共享）；本工作流不需要任何 secrets |
| 用途 | 每次代码变更自动跑：构建 + 单元测试 + Docker 镜像构建验证 |

### 流水线结构

```
push 到 main / PR / 手动触发
   │
   ▼
┌─ docker-build（唯一 job）──────────────────┐
│ checkout → setup-buildx                   │
│ → 构建后端镜像（容器内跑 dotnet test）      │
│ → 构建前端镜像（容器内跑 ng test）          │
└───────────────────────────────────────────┘
```

任何一步失败 → job 失败 → 整个 workflow 标红。测试失败会直接中断镜像构建，因此一个 job 就完成了"编译 + 单元测试 + 镜像可出"的全部验证。

### 设计原则：Dockerfile 是"唯一构建真相"

| 验证项 | 在哪里执行 |
|--------|-----------|
| 依赖安装 | Dockerfile build 阶段（`dotnet restore` / `npm ci`） |
| 编译 | Dockerfile build 阶段（`dotnet publish` / `ng build`） |
| 单元测试 | Dockerfile build 阶段（`dotnet test` / `ng test --watch=false`，测试失败即构建失败） |
| 镜像可出 | CI 的 `docker build` |

好处：构建逻辑只写一处；**本地 `docker build` 与 CI 结果完全一致**；workflow 极简、免费额度消耗更少。
代价：测试日志嵌在镜像构建日志中；首次构建要拉 base 镜像（gha 层缓存后二次运行很快）。

### 任务详解

| 步骤 | 作用 |
|------|------|
| `actions/checkout@v5` | 把仓库代码克隆到虚拟机（每个 workflow 的第一步） |
| `docker/setup-buildx-action@v4` | 启用 BuildKit 构建器，是 `cache-from/to` 层缓存的前置 |
| `docker/build-push-action@v7`（后端） | 构建后端镜像：restore → `dotnet test` → publish |
| `docker/build-push-action@v7`（前端） | 构建前端镜像：`npm ci` → `ng build` → `ng test` → nginx 托管 |

镜像构建的关键参数：

| 参数 | 值 | 原因 |
|------|----|------|
| `context` | `./src/backend` / `./src/frontend/client` | 后端必须 `src/backend`：Dockerfile 里 `COPY` 了 `services/` 与 `shared/`，路径相对 context 解析 |
| `file` | 后端需显式指定 Dockerfile 路径 | Dockerfile 不在 context 根 |
| `push` | `false` | 只构建验证，不推送（推送需 registry + secrets） |
| `cache-from/to` | `type=gha,scope=backend/frontend` | 层缓存存 GitHub Actions，二次运行加速；`scope` 区分两个镜像避免缓存冲突 |

### 关键概念速查

| 概念 | 说明 |
|------|------|
| `on` | 触发器：`push` / `pull_request` / `schedule`（cron，UTC 时区）/ `workflow_dispatch`（手动） |
| `jobs` | 任务，默认并行；`needs` 声明依赖（数组可依赖多个）——当前 CI 只有一个 job 未用到，多 job 工作流常用 |
| `runs-on` | 运行环境：`ubuntu-latest` / `windows-latest` / `macos-latest` |
| `steps` | 步骤列表，两种形式：`run`（shell 命令）、`uses`（复用 Marketplace Action） |
| `with` | 传给 Action 的参数（类似函数入参） |
| `secrets` | 机密配置，存于仓库 Settings → Secrets，YAML 用 `${{ secrets.XXX }}` 引用，禁止明文写密码 |
| `${{ }}` | 表达式，引用 secrets / env / 上下文（`github.ref`、`github.sha` 等） |
| `actions/checkout` | 拉代码，几乎每个 workflow 第一步（当前 v5） |

### 常见坑

- 忘记 `actions/checkout` → 找不到代码
- Docker 构建 context 写错 → `COPY` 找不到 `shared/`（后端 context 必须是 `src/backend`）
- `schedule` 是 UTC 时区，注意换算
- secrets 未配置就引用 → `Unrecognized named-value: 'secrets'`
- 私有仓库每月 2000 分钟免费，公共仓库配额更宽

### 后续扩展

1. 推送镜像到 Azure Container Registry（`docker/login-action@v3` + `push: true` + `ACR_USERNAME`/`ACR_PASSWORD` secrets）
2. 部署到 Azure Container Apps（`azure/login@v2` + `azure/container-apps-deploy@v2`）
3. 前端部署到 Azure Static Web Apps 免费计划（`Azure/static-web-apps-deploy`）
4. 拆分 `deploy.yml`（部署）与 `ci.yml`（构建测试），部署 job 用 `needs` 串联在 CI 之后
