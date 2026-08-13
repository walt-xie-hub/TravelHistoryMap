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
