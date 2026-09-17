# 网络与边缘

相关决策：ADR-0019（信任边界）。生产环境的唯一真相是 **Azure Container Apps**。

## 现状

### 网关（`src/gateway/nginx.conf.template`）

| 项 | 现状 |
|---|---|
| 监听 | `listen 80`——容器本身只讲 HTTP，TLS 由 ACA 边缘终止 |
| 鉴权 | **无** `auth_request`，不注入身份头 |
| 限流 | **无** `limit_req` / `limit_conn`（全仓无命中） |
| 请求头 | 只设 `X-Forwarded-For` / `X-Forwarded-Proto`；**不设置 `Host`**；不剥除客户端伪造的 `X-User-*` / `X-Internal-*` |
| 安全头 | 有 HSTS（`max-age=31536000; includeSubDomains`）、`X-Content-Type-Options`、`X-Frame-Options`、`Referrer-Policy`；**无 CSP、无 Permissions-Policy**；**无 `server_tokens off`** |
| 上游 TLS | `proxy_ssl_verify off`（注释理由：内网信任边缘 CA） |
| Body | `client_max_body_size 12m`（对应 10MB 上传） |
| 路由 | `/api/travels`、`/api/share-snapshots` → travel-service；**`/api/` 前缀全量** → user-service；`/` → client |

> HSTS 是在**明文响应**上也会下发的（`always`）——若 ACA 边缘确实把 80 重定向到 443，这不构成漏洞，但需要实测确认（官方文档称 HTTP ingress 默认重定向到 443）。

### ACA 环境

- 只有 gateway 是 `external`，其余（client / user-service / travel-service）都是 `internal`。
- **环境未接 VNet**（`az containerapp env create` 未传子网）→ 无 NSG、无 Azure Firewall、无私有端点；且**网络类型创建后不可改**。
- `internal` 的语义是"同一环境内的容器应用可达"——**不是**"只有网关能到我"。

### 数据库

- `deploy-azure.sh` 用 `az postgres flexible-server create --public-access Enabled`，无 IP 列表、无 Private Link。
- 连接串未指定 `SslMode`。
- 本地 `database/postgres/postgresql.conf` 为 `listen_addresses = '*'`、`#ssl = off`（注释态＝默认不加密）；`pg_hba.conf` 对 local / `127.0.0.1` / `::1` 是 **`trust`**，远程 TCP 为 `scram-sha-256`；compose 把 `5432:5432` 发布到宿主。

### 前端容器

- `src/frontend/client/nginx.conf` 只有 SPA fallback 与两条 `Cache-Control`，**无任何安全响应头**（当 client 不经网关被访问时没有纵深防护）。

### dev compose 暴露面

`4200`、`8080`、`8081`、`5432`、`16686`、`4317/4318/8889`、`9090`、`3100`、`3000` 全部发布到宿主且绑定 `0.0.0.0`（观测栈的部分见 `observability-and-audit.md`）。

## 目标形态

### 网关职责（ADR-0019）

1. **TLS 强制**：明文请求一律 301 到 HTTPS；HSTS 只在 HTTPS 响应上下发；开启 `server_tokens off`。
2. **请求规范化**：显式设置 `Host`；`X-Forwarded-*` 由网关重写（而非追加客户端提供的值）；**剥除** `X-User-*`、`X-Internal-*`、`X-Real-IP` 等未经自己验证的身份头。
3. **限流**（已实现，认证端点单独限速）：

   | 目标 | 机制 | 值 |
   |---|---|---|
   | `/api/auth/login`、`/api/auth/captcha`、`/api/auth/register` | `limit_req zone=auth burst=5 nodelay` + `limit_req_status 429` | 10 r/m per client IP |

   **只对认证路径启用，是有意的取舍**：ACA 边缘终止 TLS，网关看到的 `$remote_addr` 是边缘代理而不是终端用户，按它限流等于把所有用户算成同一个桶（一限全限）。因此限流的 key 取自 `X-Forwarded-For` 的**最右一个**（由边缘追加、可信），左侧值来自客户端、不可信。把范围限制在认证路径上，是为了让"限流配置写错"的最坏影响只落在登录/验证码/注册上，而不是整站。

   **全局 `limit_conn` 与 `/api/*` 限速暂缓到 P1**：它们的收益低于风险（多标签页/SPA 资源并发容易被误伤），且同样依赖上面的客户端 IP 提取，需要先在真实流量下确认提取正确。

   限流是**纵深防御**，不能替代 `authentication.md` 里的失败计数与锁定（前者防洪水，后者防定向爆破）。
4. **安全头**：CSP（本期新增）、`X-Content-Type-Options`、`X-Frame-Options`、`Referrer-Policy`、`Permissions-Policy`。CSP 起始建议：`default-src 'self'`，按实际需要放行 AMap（`https://webapi.amap.com`、`https://*.amap.com`）与图片源；先用 `Content-Security-Policy-Report-Only` 观察一轮再强制。
   - **client 的 nginx 也要加**（它自己也是一个 HTTP 服务）。
   - 加 CSP 是本期**替代性缓解**：PKCE 推迟到 P2，意味着第一期内 access token 仍存 `localStorage`，CSP 是这段窗口里唯一能实质降低 XSS 影响的措施。
5. **Body 与超时**：复核 `client_max_body_size`、`proxy_read_timeout` 与上传上限的一致性（应用侧图片上限 10MB、每记录最多 9 张）。
6. **上游校验**：`proxy_ssl_verify off` 保持现状可接受（平台内网），但需在注释里写明理由；若未来出现跨环境调用，必须打开校验。
7. **路由**：新增 `/identity/*` → identity-service（显式列在 `/api/` 之前，避免被前缀规则吞掉）。

### 暴露面约束

- 生产**只允许 gateway 为 `external`**；client 与所有后端保持 `internal`。
- 新增服务一律 `internal`，且不得登记为环境级 HTTP 路由的目标。
- 服务的运维端点（`/swagger`、`/metrics`、`/health`）若必须可达，走显式的内部路径，不要指望"没人知道地址"。

### 数据库收口

- 生产把 `--public-access` 收窄（Azure 服务内访问或最小 IP 集合），首选 Private Link / VNet 集成（随环境重建一起做，见 P2）。
- 连接串**显式指定 `SslMode=Require`**（Azure Flexible Server 支持 TLS）。
- 本地 compose 的 `5432:5432` 改为 `127.0.0.1:5432:5432`；`pg_hba.conf` 的 `trust` 仅对容器内 socket 保留（不要因为方便而对宿主开放无密码访问）。

## 检查项

### P0

- [ ] 网关：补 `limit_req` / `limit_conn`（至少覆盖登录、验证码、注册）。
- [ ] 网关：显式设置 `Host`、剥除 `X-User-*` / `X-Internal-*`、`server_tokens off`。
- [ ] 加 CSP（先 Report-Only 一轮）与 `Permissions-Policy`；client 的 nginx 同步加安全头。
- [ ] 实测确认 HTTP→HTTPS 重定向确实生效，且 HSTS 只在 HTTPS 响应上出现。
- [ ] PostgreSQL：收窄公网访问 + 连接串强制 `SslMode`。
- [ ] dev compose 的 `5432` 与观测栈端口改为绑定 `127.0.0.1`。

### P1

- [ ] 自定义域名（让 OIDC issuer 稳定，不再依赖 ACA 生成的 FQDN）。
- [ ] 网关超时与 body 上限复核；`/identity/*` 路由上线并验证优先级。
- [ ] 确认生产上没有任何后端应用被设为 `external` 或登记为环境级路由目标（加一条部署后自检）。

### P2

- [ ] ACA 环境重建为 VNet 集成（**创建后不可改，必须重建**）→ 之后才可能用 NSG / 私有端点做网络层隔离。
- [ ] 边缘引入 Azure Front Door 或 Application Gateway WAF（托管规则 + DDoS 缓解）。
- [ ] 若引入对象存储承载媒体，改为私有容器 + 短期签名 URL。

## 验收方式

- 连续 20 次快速请求 `/identity/login` → 出现 429，且不影响其他 IP。
- `curl -I https://<gw>/` 返回 CSP、`X-Content-Type-Options`、`server_tokens` 已关闭（无 `Server: nginx/1.x`）。
- 发一个带 `X-User-Id: 1` 的请求 → 后端日志显示该头未被采纳（且服务不读取任何此类头）。
- 从宿主机以外访问 `5432` → 连接失败；从应用连接 → 成功且握手加密。
- 直连任一后端 `internal` FQDN 的 `/metrics`、`/swagger` → 期望不可达（或需要身份）。

## 附录：k8s 恢复清单（当前已冻结）

`infra/k8s/**` 目前是**未完成的本地实验**，与网关行为不一致。若将来恢复维护，先补齐以下各项：

1. **Ingress 无 TLS**：`base/client/ingress.yaml` 没有 `tls:` 段，也没有任何 annotations（无 `ssl-redirect`、无 `limit-rps`）。
2. **路由不一致**：Ingress 只按 `/api` 前缀把 `share-snapshots` 打到 user-service（404），而网关转发给 travel-service。
3. **就绪探针会永远失败**：`user-service` Deployment 的 readinessProbe 打 `/`，但该服务不映射 `/`。
4. **prod overlay 无法部署**：镜像指向 `registry.example.com` 占位，且**没有 `imagePullSecrets`**。
5. **引用了不存在的组件**：`configmap.yaml` 把 `OTEL_EXPORTER_OTLP_ENDPOINT` 指向集群内没有的 `otel-collector`。
6. **缺安全基线**：无 `NetworkPolicy`、无 `securityContext`（`runAsNonRoot`、`readOnlyRootFilesystem`、`allowPrivilegeEscalation: false`、drop capabilities）、无 `resources` limits、无 ServiceAccount/RBAC。
7. **`client` Deployment 硬编码 `imagePullPolicy: Never`** 写在 base 里；dev overlay 又把三个 Deployment 全改成 `Never`。
8. **数据目录与开发环境共用**：PV 的 `hostPath` 指向 `database/postgres`，与 compose 挂载同一份物理文件——同一时间只能跑一套。
9. 恢复前需重新确认：Postgres 用 StatefulSet + 真实存储类（而非 `hostPath`），并补 RBAC 与资源限制。
