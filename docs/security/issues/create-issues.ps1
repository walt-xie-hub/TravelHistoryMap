<#
一次性创建 `docs/security/README.md` 检查项总表里的 10 条 issue。

用法：
    # 需先安装并登录 GitHub CLI：https://cli.github.com/
    gh auth login
    pwsh -File docs/security/issues/create-issues.ps1

    # 只预览不创建
    pwsh -File docs/security/issues/create-issues.ps1 -DryRun

说明：
  - 已存在同名 issue 会跳过（可重复执行）。
  - 仓库默认取 walt-xie-hub/TravelHistoryMap，可用 -Repo 覆盖。
  - 正文通过临时文件以 --body-file 传入，避免 PowerShell 参数引用问题。
#>
[CmdletBinding()]
param(
    [string]$Repo = 'walt-xie-hub/TravelHistoryMap',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Error 'gh 未安装。请先安装 GitHub CLI（https://cli.github.com/）并执行 gh auth login。'
    return
}

$issues = @(
    @{
        Title = 'P0 数据库口令已泄露（公开仓库历史）——立即轮换并收口暴露面'
        Label = 'ready-for-human'
        Body  = @'
## 背景（现状证据，2026-09-17 确认）

- 仓库 `walt-xie-hub/TravelHistoryMap` 是 **public**。
- 初始提交 `8faf6c5`（2026-07-26）写入 `infra/k8s/base/secret.yaml`，内含 `Db__Password: "123456"`；该文件在 `390eb82`（2026-09-07）被删除，**但历史永远可取出**。
- **本地 `.env` 的 `DB_PASSWORD` 已确认就是这个值** —— 即一个公开了 7 周的口令，**仍是当前有效口令**。
- `.env` 本身**从未被提交**（历史里只有 `.env.example`）——这是唯一的好消息。
- `deploy-azure.sh` 用 `az postgres flexible-server create --public-access Enabled`（无 IP 列表、无 Private Link），连接串**未指定 `SslMode`**。
- `database/docker-build-script.txt` 同样是 `POSTGRES_PASSWORD=123456`（未跟踪，但磁盘可见）。
- `pg_hba.conf` 对 local / `127.0.0.1` / `::1` 是 `trust`；compose 把 `5432:5432` 发布到宿主。

## 要做什么

1. **先确定影响面**：Azure 上建库时 `deploy-azure.sh` 的 `PG_PASSWORD` 填的是不是这个值？
   - 是 → 公网可达的生产库口令已公开，按最高优先级处理；
   - 否 → 影响面限于本地开发库，但**仍然必须轮换**（同一口令跨环境复用是横向移动的燃料）。
2. **轮换 Azure**：
   `az postgres flexible-server update -g travelMap -n pg-travelmap --admin-password '<新口令>'`
   随后更新 ACA 的 `db-password` secret 并触发新 revision。
3. **轮换本地（有坑）**：`POSTGRES_PASSWORD` **只在 initdb 时生效**。`database/postgres` 已是初始化过的数据目录，改 `.env` 不会改角色口令，必须显式执行：
   `docker exec -it postgres-db psql -U appuser -d appdb -c "ALTER USER appuser WITH PASSWORD '<新口令>';"`
   再同步 `.env`。
   **不要**为了改口令删除 `database/postgres`（那是开发数据，且与 k8s PV 共用同一份物理目录）。
4. 新口令用密码学随机生成（≥24 字符），不要用可记忆短串。
5. 清掉 `database/docker-build-script.txt` 里的硬编码口令。
6. 收窄 PostgreSQL 公网访问：最小 IP 集合，或随环境重建走 VNet / Private Link。
7. 连接串**显式指定 `SslMode=Require`**。
8. 本地：compose 的 `5432` 改绑 `127.0.0.1:5432:5432`；`pg_hba.conf` 的 `trust` 只保留容器内 socket 用途。
9. **不重写 git 历史**（已被 clone 过，改写不收敛）；正确做法是把已泄露的值全部作废。
10. 可选但建议：把仓库改为 private——**不能替代轮换**，只是减少后续暴露面。

## 验收

- 用历史里的口令连接**任何**环境 → 认证失败。
- 从非 Azure 内来源连接托管库 → 连不上。
- 应用侧连接握手为加密（`SslMode=Require` 生效）。

## 参考

`docs/security/secrets-and-keys.md`（「立即处置」、轮换顺序 ①）、`docs/security/network-and-edge.md`（数据库收口）
'@
    },
    @{
        Title = 'P0 GHCR 镜像转私有并给 ACA 配置拉取凭据'
        Label = 'ready-for-agent'
        Body  = @'
## 背景（现状证据）

- `.github/workflows/ci.yml` 在构建后用 `gh api -X PATCH .../packages/container/<img> -f visibility=public` 把 4 个镜像**显式设为公开**。
- 镜像内含前端产物（含 `runtime-config.js`）与全部运行时逻辑，匿名可拉取等于把实现与依赖面完全敞开。
- `deploy-azure.sh` 已经支持 `--registry-server` / `--registry-username` / `--registry-password`，改动面很小。

## 要做什么

1. 删除 `ci.yml` 中把包设为 public 的步骤。
2. 把 4 个包改为 private（`gh api -X PATCH ... -f visibility=private`）。
3. 部署路径提供 registry 凭据：ACA 用 `--registry-*`，凭据来自 GitHub Secrets；使用**只读 `read:packages` 权限**的凭据，不要用宽权限 PAT。
4. 顺手修正文档漂移：README 的结构图与"任务详解"只写了 3 个镜像，而 CI 实际构建并部署 **4 个**（含 travel-service）。

## 验收

- 匿名 `docker pull ghcr.io/<org>/travelmap-*` → 失败。
- ACA 各 app 能正常拉到新 revision 的镜像（至少一次完整发布验证）。

## 参考

`docs/security/secrets-and-keys.md`（现状 ⑤、轮换顺序 ④）
'@
    },
    @{
        Title = 'P0 观测栈收口：端口绑定、默认口令、debug exporter 与属性脱敏'
        Label = 'ready-for-human'
        Body  = @'
## 背景（现状证据）

- `docker-compose.dev.yml` 中 `jaeger`(16686)、`prometheus`(9090)、`loki`(3100)、`grafana`(3000)、`otel-collector`(4317/4318/8889) 全部发布到宿主并绑定 `0.0.0.0`。
- `infra/observability/loki/loki-config.yaml` 为 `auth_enabled: false`（无任何鉴权）。
- Grafana 的 `GF_SECURITY_ADMIN_PASSWORD=admin` **已提交**，README 访问表还明文写出 `admin/admin`。
- Prometheus 启动参数含 `--web.enable-lifecycle`（允许 `POST /-/reload` 触发配置重载）。
- `infra/observability/otel-collector-config.yaml` 中 `debug` exporter 的 `verbosity: detailed` **同时挂在 traces / metrics / logs 三条管线**上；删除 `http.request.header.authorization` 的 `attributes` 段**被注释掉**。
- 应用侧请求日志只记 Method/Path/Status/Elapsed，**不记请求体、不记 Authorization 头**（这部分是好的）。

## 要做什么

1. 所有 dev 端口改为绑定 `127.0.0.1`。
2. 去掉 Grafana 默认口令（或改为由 `.env` 提供的变量）；从 `README.md` 里删除 `admin/admin` 的明文说明。
3. 去掉 `--web.enable-lifecycle`。
4. `debug` exporter 只保留在本地配置中；生产/共享环境配置中不得出现 `verbosity: detailed`。
5. **打开** `attributes` 过滤：删除 `http.request.header.authorization`、`cookie`、`set-cookie` 以及任何 token 字段。

## 验收

- 从宿主机以外的机器访问 Grafana / Loki / Jaeger / Prometheus 端口 → 连接被拒。
- 在落库的 trace / log 中搜 `authorization`、`Bearer ` → 无命中。
- `docker logs otel-collector` 不再打印完整 span / log 内容。

## 参考

`docs/security/observability-and-audit.md`（检查项 P0）、`docs/security/threat-model.md`（攻击面表）
'@
    },
    @{
        Title = 'P0 生产遥测端点：去掉指向不存在的 collector，改走 ACA 环境级托管 agent'
        Label = 'ready-for-agent'
        Body  = @'
## 背景（现状证据）

- `infra/k8s/base/configmap.yaml` 把 `OTEL_EXPORTER_OTLP_ENDPOINT` 指向集群内**不存在**的 `http://otel-collector:4317`。
- `deploy-azure.sh` 未设置该变量，`Shared.Observability/Telemetry.cs` 回落到 `http://localhost:4317` —— 生产上没有任何 collector，导出只会静默失败并产生噪音。
- 采样为 `AlwaysOnSampler`（100%）。

## 要做什么

1. 改用 **ACA 环境级托管 OpenTelemetry agent**（官方支持，零额外 compute 成本）：
   `az containerapp env telemetry app-insights set --resource-group <rg> --name <env> --connection-string <cs> --enable-open-telemetry-traces true --enable-open-telemetry-logs true`
   agent 会**自动注入** `OTEL_EXPORTER_OTLP_ENDPOINT`，应用侧几乎不用改（只支持 gRPC，本项目正好是 OTLP/gRPC）。
2. 移除 configmap 中指向不存在组件的端点。
3. 采样改为按比例采样 + 错误全采，避免 100% 采样烧完额度。
4. 已知限制要写进文档并在验收时确认：
   - **App Insights 端点不接受 metrics**（只收 logs + traces）→ metrics 要么交由 managed Prometheus 抓 `/metrics`，要么本期明确不采集，不接受"采集失败且无人知道"的中间状态；
   - agent 配置**不支持 Key Vault 引用**；若 App Insights 启用 Entra-only 认证，agent 将收不到数据（必须保留本地认证）。

## 验收

- 生产环境在 Application Insights 里能看到 traces 与 logs。
- 应用日志中不再出现 OTLP 导出失败。
- `grep -rn "otel-collector" infra/` 不再指向不存在的组件。

## 参考

`docs/security/observability-and-audit.md`（遥测落点）
'@
    },
    @{
        Title = 'P0 网关加固：Host、限流、CSP、头剥离与 HSTS 生效条件'
        Label = 'ready-for-human'
        Body  = @'
## 背景（现状证据）

`src/gateway/nginx.conf.template`：

- `listen 80`（TLS 由 ACA 边缘终止）；**无 `limit_req` / `limit_conn`**；无 `auth_request`（符合 ADR-0019，网关不做鉴权）。
- **不设置 `Host`**；不剥除客户端伪造的 `X-User-*` / `X-Internal-*`。
- 已有 HSTS、`X-Content-Type-Options`、`X-Frame-Options`、`Referrer-Policy`；**无 CSP、无 Permissions-Policy、无 `server_tokens off`**。
- HSTS 用 `always`，在明文响应上也会下发 —— 需实测确认 ACA 是否确实把 80 重定向到 443。
- `client_max_body_size 12m`（对应 10MB 上传）。
- `src/frontend/client/nginx.conf` **无任何安全响应头**。
- `/api/` 是前缀全量转发，因此**新增在 `/api/` 下的端点会自动公开**。

## 要做什么

> **状态（2026-09-17）**：第 1–4 项与第 7 项中的 HSTS 条件已实现（见 `docs/security/README.md` 的「实现状态」）；第 5、6 项与生产实测仍未完成。

1. ✅ 显式设置 `Host`；`X-Forwarded-For` 只传边缘追加的真实客户端 IP；**剥除**身份类请求头（`X-Real-IP`、`X-User-*`、`X-Internal-*`、`X-Forwarded-User` 等——nginx 不支持通配符，故显式枚举）。
2. ✅ 加限流（只覆盖认证路径；10 r/m per client IP，`burst=5 nodelay`，429）。
   - 为什么不是"所有 `/api/*` + 全局 `limit_conn`"：ACA 边缘终止 TLS，`$remote_addr` 是边缘而不是用户，按它限流会一限全限；`/api/*` 与并发限制的收益低于误伤风险，留到 P1。
   - 客户端 IP 取自 `X-Forwarded-For` 最右一段（**该假设来自官方文档，需在生产实测确认**）。
3. ✅ 加 **CSP**（当前 `Report-Only`）与 `Permissions-Policy`；client 的 nginx 同步加。
4. ✅ `server_tokens off`。
5. ⬜ 新增 `/identity/*` 路由（必须显式列在 `/api/` 规则之前）——依赖 identity-service 落地，**本次未做**（模板里引用未定义的变量会让 nginx 启动失败）。
6. ⬜ 复核超时与 body 上限，与应用侧（单图 10MB、每记录最多 9 张）一致。
7. ✅/⬜ HSTS 改为只在 `X-Forwarded-Proto: https` 时下发；80→443 重定向与 CSP 上报内容需在生产实测。

> 为什么本期就加 CSP：PKCE 推迟到 P2，第一期内 access token 仍存 `localStorage`，CSP 是这段窗口里唯一能实质降低 XSS 影响的措施。

## 验收

- 连续 20 次快速请求 `/api/auth/login` → 出现 429，且不影响其他 IP（需在生产确认限流 key 取到的是真实客户端 IP，而不是 ACA 边缘 IP）。
- `curl -I https://<gw>/` 返回 CSP 等安全头，且 `Server` 头不暴露 nginx 版本。
- 带 `X-User-Id: 1`、`X-Real-IP: 1.2.3.4` 的请求 → 这些头未到达后端。
- 从非白名单 Origin 的跨域请求在生产被拒。

## 参考

`docs/security/network-and-edge.md`（检查项 P0）、ADR-0019
'@
    },
    @{
        Title = 'P0 认证路径必须检查 IsActive（停用账号不得换到令牌）'
        Label = 'ready-for-agent'
        Body  = @'
## 背景（现状证据）

- `AppUser.IsActive` 字段存在（`user-service/src/User.Domain/Entities/AppUser.cs`）。
- 但登录流程**从不检查它**：只有 `user?.PasswordHash is null || !_passwordHasher.Verify(...)` 才会拒绝。
- 也没有任何停用账号的接口 —— 也就是说这个字段目前既不被写、也不被读，等于不存在。

## 要做什么

- identity-service 的首个可用增量中，认证路径必须检查 `IsActive`；停用账号**不得**换取令牌。
- 在 identity-service 上线之前，先在 user-service 现有登录路径补上该检查（成本≈0，因为已经在读用户行）。
- 同时明确：停用账号时 user-service 需自增 `CredentialVersion`，使既有 refresh 一并失效（见 ADR-0020）。

## 验收

- 用 `IsActive = false` 的账号登录 → **401**（不是 200）。
- 停用后，原先持有的 refresh 再换新 → 被拒。

## 参考

`docs/security/authentication.md`（检查项 P0）、ADR-0020
'@
    },
    @{
        Title = 'P1 identity-service 落地：契约与骨架 → OIDC discovery + JWKS + 登录与 refresh 轮换'
        Label = 'ready-for-agent'
        Body  = @'
## 背景

- 契约与决策已定：ADR-0020（凭据归属）、ADR-0021（令牌模型）、ADR-0019（端点路径规则）。
- 端点契约、claims 最小集、失败治理与审计事件清单见 `docs/security/authentication.md`。

## 要做什么（按序）

1. **契约与骨架**：`src/backend/services/identity-service/`（程序集 `Identity.Api`）、`backend.slnx` 条目、Dockerfile、只含 `/health` 与配置骨架的 `Program.cs`。
2. **表结构**：`ServiceClients`（`ClientSecretHash` **只存哈希**）、`RefreshTokens`（`TokenHash` 只存哈希、`FamilyId`、`IssuedAt`/`ExpiresAt`/`RevokedAt`/`ReplacedByHash`/`CredentialVersionAtIssue`）。签名密钥**不入库**（来自 Key Vault，见 ADR-0023）。
3. **只读凭据**：identity-service 只读 `Users` 的 `Id`/`Email`/`PasswordHash`/`IsActive`/`CredentialVersion`，**不写 `Users`**（ADR-0020）。哈希用框架 `PasswordHasher` 互认，不共享代码。
4. **OIDC discovery + JWKS**：`/identity/.well-known/openid-configuration`、`/identity/jwks`（多 `kid` 并存）；issuer 为显式配置项（ADR-0021）。
5. **登录**：`/identity/captcha`（验证码从 user-service 迁来，随机数改 `RandomNumberGenerator`）、`POST /identity/login`（验证码 + 凭据 → access + refresh），**必须检查 `IsActive` 与 `CredentialVersion`**。
6. **刷新**：`POST /identity/token`，7 天、每次轮换、**旧 token 复用即整族撤销**；`POST /identity/logout`、`POST /identity/revoke`。
7. **注册与档案不动**：注册与 `/api/users/me*` 仍归 user-service。
8. **审计**：按 `observability-and-audit.md` 的事件清单埋点，绝不记录口令与令牌原文。

## 约束

- 内部/管理端点用 `/internal/*`，不得落在 `/api/*` 下（ADR-0019）。Client 注册**先不提供管理端点**，用部署期种子或运维脚本（少一个管理面）。
- 前端在第一期仍用自有登录端点换令牌；PKCE 属 P2，不在本 issue 内。

## 验收

- 全部通过 `docs/security/authentication.md` 的"验收方式"小节。
- 停用账号、改密码、refresh 复用三种场景的行为与文档一致。

## 参考

ADR-0019 / 0020 / 0021 / 0023、`docs/security/authentication.md`
'@
    },
    @{
        Title = 'P1 RS256/JWKS 上线、受限共存，并在 7 天内删除 HS256'
        Label = 'ready-for-agent'
        Body  = @'
## 背景

- 现状：user-service 自签 HS256，travel-history 用**同一把对称密钥**校验；`deploy-azure.sh` 根本没有注入 `Jwt__Key`（缺 key 启动即抛），说明线上密钥只存在于手工配置中、无法追踪也无法轮换。
- 目标模型见 ADR-0021。

## 要做什么（按序）

1. travel-history 先上线「JWKS 验签（RS256）」**并暂时保留** HS256 校验（同一份 `TokenValidationParameters` 支持两种）。
2. identity-service 开始签发 RS256，**立即停止签发 HS256**。
3. **共存窗口上限 7 天**（= 最长 token 生命周期）。到期必须删除 HS256 校验分支、`Jwt:Key` 配置、compose 的 `JWT_KEY` 与 `.env.example` 对应项。
4. 更新 ADR-0005 状态为 superseded by ADR-0021（只标状态，不改写内容）。
5. 复核 `aud` 精确匹配（用户 token = `travel-map-client`）、`ClockSkew` 收紧（当前 1 分钟）、以及 `ValidAlgorithms` 显式白名单。

## 验收

- `grep -rn "HmacSha256" src/backend` → 无命中。
- `grep -rn "Jwt__Key\|JWT_KEY" .` → 无命中（文档除外）。
- 部署侧不再需要任何对称签名密钥。
- 老 token 过期后，仅剩 RS256 路径生效。

## 参考

ADR-0021、`docs/security/authentication.md`（迁移小节）
'@
    },
    @{
        Title = 'P1 汇总：密钥托管、审计、登录失败治理、CORS、运维端点、域名与 Client 注册表'
        Label = 'needs-triage'
        Body  = @'
汇总 P1 条目（详见 `docs/security/README.md` 的检查项总表与各分片）。请按需拆分为独立 issue。

## 密钥与机密

- [ ] Key Vault 建立 + 各容器应用分配托管身份；机密清单逐项迁入（`secrets-and-keys.md`）。
- [ ] 签名私钥改为应用内 SDK 读取，实现多 `kid` 并存与**不重启轮换**；KV 不可用时 identity-service **fail closed** 拒绝启动，资源服务使用缓存 JWKS 并在缓存过期且拉取失败时拒绝令牌。
- [ ] Client secret 只存哈希；推进服务注册不提供管理端点。
- [ ] AMap 域名白名单收紧到实际域名（可选重新生成 key）。
- [ ] 人工确认已提交的 `doc/*.docx` 与 `log/**` 中无残留凭据或环境回显。

## 认证与授权

- [ ] 认证审计事件落地（6 个认证事件），并确认落到可查询、可留存的后端。
- [ ] 登录失败计数与锁定（按账号 + 来源 IP 滑动窗口，阈值与时长写进 `authentication.md`）。
- [ ] 验证码随机数改 `RandomNumberGenerator`（随 identity-service 迁移完成）。
- [ ] 归属规则收敛为一处实现，并覆盖 `DeletedAt`（含 `TravelImageService.OwnsRecordAsync`）。
- [ ] 分享令牌复核：随机性、长度、过期与吊销、返回体不含图片/精确坐标/内部标识。
- [ ] CORS 改为配置项；移除硬编码 localhost 与 `SetIsOriginAllowed`。

## 网络与观测

- [ ] `/swagger`、`/openapi/v1.json` 加环境判断（仅 Development）；`/metrics` 收紧为仅内部可达。
- [ ] 自定义域名，使 OIDC issuer 稳定。
- [ ] 采样改为按比例 + 错误全采；确认 trace 中 SQL span 不含参数值。
- [ ] 部署后自检：没有任何后端应用被设为 `external` 或登记为环境级路由目标。

## 参考

`docs/security/README.md`（P1 表）
'@
    },
    @{
        Title = 'P2 汇总：PKCE、Dapr mTLS、WAF、VNet、指标入库与 k8s 恢复清单'
        Label = 'needs-triage'
        Body  = @'
汇总 P2 条目（详见 `docs/security/README.md` 的检查项总表与各分片）。请按需拆分为独立 issue。

## 认证

- [ ] 前端改造为 Authorization Code + **PKCE** + `id_token` + `/userinfo`；令牌改内存存储（届时关闭"localStorage 敞口"这一显式风险）。
- [ ] 口令策略提高到 12 位并引入常见口令黑名单。
- [ ] 找回密码 / 邮箱验证流程（需要新的令牌语义与邮件通道）。

## 服务身份与内部通道

- [ ] 首个真实 S2S 调用落地时启用 **Dapr**（ACA 上 GA，自带 mTLS 与加密、证书由平台轮换），并把 Dapr trace 接到观测后端。
- [ ] 若跨服务调用增长到需要调用拓扑，评估服务网格而非手工 mTLS。

## 网络与边缘

- [ ] ACA 环境重建为 **VNet 集成**（创建后不可改，必须重建）→ 之后才可能用 NSG / 私有端点做网络层隔离。
- [ ] 引入 Azure Front Door 或 Application Gateway **WAF**（托管规则 + DDoS 缓解）。
- [ ] 媒体改对象存储 + 私有容器 + 短期签名 URL。

## 观测

- [ ] metrics 正式入库（Azure Monitor managed Prometheus）并配告警：登录失败率、锁定次数、`refresh_reuse_detected`、5xx 率。

## k8s（已冻结，恢复维护前必须补齐）

- [ ] Ingress 加 TLS 与 annotations（`ssl-redirect`、`limit-rps`）。
- [ ] 修 `/api/share-snapshots` 路由（当前误打到 user-service）。
- [ ] 修 user-service 的 readinessProbe（当前打 `/` 会永远失败）。
- [ ] prod overlay：真实 registry + `imagePullSecrets`。
- [ ] 移除指向不存在 `otel-collector` 的 configmap 配置。
- [ ] 补安全基线：`NetworkPolicy`、`securityContext`（`runAsNonRoot`、`readOnlyRootFilesystem`、`allowPrivilegeEscalation: false`、drop capabilities）、`resources` limits、ServiceAccount/RBAC。
- [ ] 去掉 base 里 `client` Deployment 硬编码的 `imagePullPolicy: Never`。
- [ ] Postgres 用真实存储类替代 `hostPath`，并解决与 compose 共用同一份 PGDATA 的问题。

## 其他

- [ ] 密钥轮换日历（签名密钥、DB 口令、Client secret）。
- [ ] 收窄 CI 的 Azure 联邦凭据角色（当前文档指导分配 Contributor）。

## 参考

`docs/security/README.md`（P2 表）、`docs/security/network-and-edge.md`（附录：k8s 恢复清单）
'@
    }
)

# 拉一次既有标题，实现幂等（可重复执行）
$existingTitles = @()
try {
    $existingTitles = gh issue list --repo $Repo --state all --limit 500 --json title --jq '.[].title'
    if ($null -eq $existingTitles) { $existingTitles = @() }
} catch {
    Write-Warning "读取既有 issue 列表失败，将不做去重：$_"
}

$created = 0
$skipped = 0

foreach ($issue in $issues) {
    if ($existingTitles -contains $issue.Title) {
        Write-Host "跳过（已存在）: $($issue.Title)" -ForegroundColor DarkGray
        $skipped++
        continue
    }

    if ($DryRun) {
        Write-Host "[DryRun] 将创建: $($issue.Title)  [label: $($issue.Label)]" -ForegroundColor Cyan
        continue
    }

    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($tmp, $issue.Body, (New-Object System.Text.UTF8Encoding($false)))
        $url = gh issue create --repo $Repo --title $issue.Title --body-file $tmp --label $issue.Label
        Write-Host "已创建: $($issue.Title) -> $url" -ForegroundColor Green
        $created++
    }
    finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host "完成：创建 $created 条，跳过 $skipped 条，目标仓库 $Repo。"
if ($DryRun) { Write-Host '（DryRun 模式，未实际创建）' }
