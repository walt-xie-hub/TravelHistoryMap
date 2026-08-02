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
│  exporters:               │
│    otlp/jaeger ──────────────> Jaeger  (Traces)
│    prometheus  <── HTTP scrape ─ Prometheus (Metrics via /metrics)
│    debug       ──────────────> Console (docker logs otel-collector)
└───────────────────────────┘
                                  ┌──────────┐
                                  │ Grafana   │ ← 数据源来自 Prometheus
                                  └──────────┘
```

### 访问地址

| 服务 | URL | 说明 |
|------|-----|------|
| **Swagger UI** | http://localhost:8080/swagger | API 文档 + 交互测试 |
| **Jaeger UI** | http://localhost:16686 | 分布式追踪查询 (Tracing) |
| **Prometheus** | http://localhost:9090 | 时序指标查询 (Metrics) |
| **Grafana** | http://localhost:3000 | 预置仪表板 (admin / admin) |
| **`/metrics`** | http://localhost:8080/metrics | Prometheus 抓取端点（应用直接暴露） |
| **`/health`** | http://localhost:8080/health | 健康检查 |
| **Collector debug** | `docker logs otel-collector` | Collector 控制台输出完整 trace 内容 |

### 配置分布

| 配置 | 文件 | 说明 |
|------|------|------|
| 应用侧 Exporter / Processor | `src/backend/shared/Shared.Observability/Telemetry.cs` | OTLP、Prometheus 导出器 + AlwaysOnSampler |
| Collector receiver/processor/exporter | `infra/observability/otel-collector-config.yaml` | OTLP 接收 → batch/过滤 → Jaeger + Prometheus 导出 |
| 开发环境变量 | `docker-compose.dev.yml` | `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317` |
| 生产环境变量 | `infra/k8s/base/configmap.yaml` | K8s OTLP 端点 `http://otel-collector:4317` |
| Prometheus 抓取配置 | `infra/observability/prometheus/prometheus.yml` | 抓取目标 `server:8080/metrics` |
| Grafana 数据源 | `infra/observability/grafana/datasources/datasource.yml` | Prometheus 数据源预置 |
| Grafana 仪表板 | `infra/observability/grafana/dashboards/` | ASP.NET Core 指标预置仪表板 |

### 数据流

```
App → Collector (OTLP/gRPC :4317) ─┬─→ Jaeger :4317 (Traces)
                                   └─→ Console (debug exporter)
App → /metrics ←── Prometheus scrape :9090 → Grafana :3000
```

### 遥测覆盖

- **Traces**: ASP.NET Core HTTP 请求、Npgsql 数据库 SQL 命令（含耗时）
- **Metrics**: HTTP 请求速率/延迟/活跃连接、GC 堆/回收频率、线程池、CPU、内存
- **Logs**: 结构化日志通过 OTLP 导出
- **Collector debug exporter**: `docker logs otel-collector` 查看完整 span 内容
- **App ConsoleExporter**: DEBUG 构建时 span 输出到 `docker logs dotnet-api-dev`
