# 机密与密钥

相关决策：ADR-0023（Key Vault + 托管身份；签名私钥走 SDK、其余走平台引用）。

## 现状

| # | 凭据 / 密钥 | 位置 | 状态 |
|---|---|---|---|
| ① | 数据库口令 | **git 历史 `8faf6c5`** 的 `infra/k8s/base/secret.yaml` 含 `Db__Password: "123456"`（删除该文件的提交并没有让口令消失）；**已确认本地 `.env` 的 `DB_PASSWORD` 就是该值**；ACA 上以 `db-password` secret 手工配置 | 🔴 **确认泄露、且仍是当前有效口令**：仓库为 **public**，该提交自 2026-07-26 起对全网可见 |
| ② | JWT 签名密钥 | `Jwt__Key` 只存在于部署侧手工配置；`.env` 的 `JWT_KEY`；`.env.example` 为占位符 | ⚠️ **不可轮换**：仓库无法追踪线上值，且 `deploy-azure.sh` 根本没注入它（缺 key 时启动即抛） |
| ③ | Grafana 管理员口令 | `docker-compose.dev.yml` 的 `GF_SECURITY_ADMIN_PASSWORD=admin`，README 访问表还明文写出 | ⚠️ 已提交的默认凭据（dev） |
| ④ | AMap key / securityJsCode | `src/frontend/client/public/runtime-config.js`（被 gitignore，但会被 `docker build` **烘进镜像产物**） | ⚠️ 浏览器端密钥本质上公开（ADR-0003 的既定取舍），但域名白名单必须收紧 |
| ⑤ | 容器镜像 | CI 用 `gh api -X PATCH ... visibility=public` 把 4 个镜像显式设为公开 | ⚠️ 任何人可匿名拉取全部运行时逻辑与前端产物 |
| ⑥ | Azure 标识符 | `ci.yml` 与 `README.md` 明文写出三个 secret 名，名字里内嵌真实的 app client id / tenant id / subscription id | 非机密，但属已公开的标识符 |
| ⑦ | 演示账号口令 | `.env` 的 `DEMO_USER_PASSWORD`；user-service 仅 Development 幂等种子 `demo@travel.local` | 仅开发环境；生产不得设置该配置 |
| ⑧ | 本地 PG 脚本 | `database/docker-build-script.txt` 含 `POSTGRES_PASSWORD=123456`（该目录被 gitignore，未跟踪） | 磁盘可见；与 ① 相关 |

补充事实（不是风险）：`database/seed_users.sql` 写入的 `PasswordHash` 是对随机串取 MD5 的结果，与应用使用的 PBKDF2 格式不同，因此这 10 万条种子用户**无法登录**，不构成弱口令面。

## 立即处置（2026-09-17 确认）

事实链：仓库是 **public** → 初始提交 `8faf6c5`（2026-07-26）写入 `Db__Password: "123456"` → 该文件在 `390eb82`（2026-09-07）被删除但**历史永远可取出** → 已确认本地 `.env` 的 `DB_PASSWORD` **就是该值**。`.env` 本身从未被提交（只有 `.env.example`），这是唯一的好消息。

因此该口令必须视为**已公开**，按以下顺序处置：

1. **先确定影响面**：Azure 上建库时 `deploy-azure.sh` 的 `PG_PASSWORD` 填的是不是这个值？
   - 是 → 公网可达（`--public-access Enabled`）的生产库口令已公开，按最高优先级处理；
   - 否 → 影响面限于本地开发库，但**仍然必须轮换**（同一口令跨环境复用是横向移动的燃料）。
2. **轮换 Azure**：`az postgres flexible-server update -g travelMap -n pg-travelmap --admin-password '<新口令>'`，随后更新 ACA 的 `db-password` secret 并触发新 revision。
3. **轮换本地（有坑）**：`POSTGRES_PASSWORD` **只在 initdb 时生效**。`database/postgres` 已是初始化过的数据目录，改 `.env` 不会改角色口令，必须显式执行：
   `docker exec -it postgres-db psql -U appuser -d appdb -c "ALTER USER appuser WITH PASSWORD '<新口令>';"`，再同步 `.env`。
   **不要**为了改口令删除 `database/postgres`——那是你的开发数据，且与 k8s PV 共用同一份物理目录。
4. 新口令用密码学随机生成（≥24 字符），不要用可记忆短串。
5. 同步清掉 `database/docker-build-script.txt` 里的硬编码口令。
6. 可选但建议：把仓库改为 private——它**不能替代轮换**，只是减少后续暴露面。
7. **不重写 git 历史**：已被 clone 过，改写既不收敛也会破坏所有本地副本；正确做法是把已泄露的值全部作废。

## 目标形态

- 所有机密与密钥进入 **Azure Key Vault**，容器应用以**系统分配的托管身份**访问。
- **签名私钥由应用内读取**（`Azure.Identity` + Key Vault SDK）：轮换要求新旧公钥并存、**不重启即可切换**。
- **其余机密走平台原生 Key Vault 引用**：零代码、ASP.NET 配置键不变、重启生效。
- Key 命名约定 `jwt-signing-<kid>`；identity-service 通过列出该前缀的 secret 得到整个 key set（**不需签名密钥表**）。

### 环境机密清单（目标态）

| 用途 | 承载 | 取用方式 | 归属服务 |
|---|---|---|---|
| DB 连接串 / 口令 | KV | 平台引用 → `ConnectionStrings__DefaultConnection` | user / travel / identity |
| 签名私钥（多 `kid`） | KV，`jwt-signing-<kid>` | 应用内 SDK 读取（**唯一需要代码的机密**） | identity |
| Client secret（服务身份） | identity 自有表，**只存哈希** | 哈希比对，无明文存放 | identity |
| 演示账号口令 | KV / ACA secret | 平台引用 → `DemoUser__Password`（仅 dev） | user |
| App Insights connection string | ACA 环境级 agent 配置 | 平台配置（官方明确**不是**安全令牌） | 环境 |
| AMap key / securityJsCode | CI Secret → 构建期生成 `runtime-config.js` | 烘进前端产物（浏览器端） | client |

### 轮换语义（签名密钥）

1. 以新 `kid` 写入新 key（`jwt-signing-<new>`）→ JWKS **同时**暴露新旧公钥；
2. 新签发只用新 key；
3. 等待**最长 access TTL**（30 分钟）后停用旧 key，但旧公钥仍需**保留到它签发的 token 全部过期**（即再等一个最长 token 生命周期）；
4. 删除旧 key 时同步从 JWKS 摘除其 `kid`。

### Key Vault 不可用时的策略

- **identity-service（签发方）fail closed**：读不到私钥就拒绝启动，绝不回退到任何内置密钥。
- **资源服务（验签方）容忍短时不可用**：使用缓存的 JWKS；缓存过期且拉不到时**拒绝令牌**（而不是放行）。

## 轮换顺序

| 顺序 | 动作 | 档 | 理由 |
|---|---|---|---|
| ① | 确认 Azure 建库口令是否同一值；随后轮换数据库口令（Azure + 本地 `ALTER USER`），并**收窄公网访问、强制 `SslMode`** | **P0 · 立即** | 口令已确认泄露且仍有效，仓库还是 public，库当前公网可达 |
| ② | 轮换签名密钥 | **P1（搭 RS256 迁移的车）** | HS256 共享密钥随迁移退役即作废，不需要单独一次轮换动作 |
| ③ | ACA 上手工配置的 secret 全部改为 KV 引用 | **P1** | 轮换只有在"密钥不再手工配置"之后才是可重复流程 |
| ④ | GHCR 镜像转私有 + ACA 配 registry 凭据 | **P0** | 一条命令级改动，堵掉供应链敞开面 |
| ⑤ | AMap：收紧域名白名单（可选重新生成 key） | **P1** | 关键不是换 key，而是白名单 |
| ⑥ | Grafana 默认口令与观测栈暴露收口 | **P0**（含于观测栈收口） | 与"观测栈绑定回环"一并做 |

**不做**：不重写 git 历史（`filter-repo` / BFG）。历史已被 clone 过，改写既不保证收敛，又会破坏所有本地副本；正确做法是**把已泄露的值全部作废**。若确实需要清理历史，单独立项并通知所有协作者。

## 检查项

### P0

- [ ] 确认并轮换数据库口令（Azure 与本地各自执行；注意本地必须 `ALTER USER`，改 `.env` 无效）；收窄 PostgreSQL 公网访问；连接串强制 `SslMode`。
- [ ] GHCR 镜像转私有，ACA 配置 registry 凭据（脚本已支持 `--registry-*`）。
- [ ] 从 `README.md` / `ci.yml` 移除明文默认凭据与内嵌 GUID 的 secret 命名（改为通用名 + 在 GitHub Secrets 中重命名）。

### P1

- [ ] Key Vault 建立 + 各容器应用分配托管身份；机密清单（见上）逐项迁入。
- [ ] 签名私钥改为应用内 SDK 读取，实现多 `kid` 并存与轮换；JWKS 暴露全部有效 `kid`。
- [ ] identity-service 的 Client secret 只存哈希；服务注册不提供管理端点。
- [ ] AMap 域名白名单收紧到实际域名。
- [ ] 复核 `log/**` 与 `doc/**` 中是否残留环境回显或凭据（`doc/*.docx` 为已提交二进制，需人工确认）。

### P2

- [ ] 引入轮换日历（签名密钥、DB 口令、Client secret 的定期轮换与到期提醒）。
- [ ] 评估把 CI 的 Azure 联邦凭据收窄到最小角色（当前文档指导分配 Contributor）。

## 验收方式

- `git log -p --all -- infra/k8s/base/secret.yaml` 里出现的口令**在任何环境都不再有效**（用旧口令连接 → 认证失败）。
- `grep -rn "Jwt__Key\|HmacSha256" .` → 无命中（迁移完成后）。
- 删除 KV 中当前 `jwt-signing-<kid>` → identity-service 拒绝启动（fail closed）；资源服务在 JWKS 缓存过期后拒绝令牌。
- KV 中新增一把 key → JWKS 立即返回两个 `kid`，且新签发 token 的 `kid` 为新值，服务重启次数为 **0**。
- 匿名 `docker pull ghcr.io/<org>/travelmap-*` → 期望失败。
