#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────
# Azure Container Apps 一次性初始化脚本
# 用途：创建资源组 / 容器应用环境 / Key Vault / PostgreSQL 免费层 / 5 个 Container App
# 前置：已登录 az（Cloud Shell 或本地 az CLI），且已注册 Microsoft.App
# 使用：PG_PASSWORD="你的密码" bash deploy-azure.sh
#
# 与 .github/workflows/ci.yml 的约定：
#   - 资源组   travelMap
#   - 环境     cae-travelmap（区域 eastus，与 CAE_NAME/CAE_REGION 一致）
#   - 镜像     ghcr.io/walt-xie-hub/travelmap-{client,user-service,travel-service,identity-service,gateway}
#   - ingress  client/user-service/travel-service/identity-service 为 internal，
#              gateway 为 external（唯一入口）
#   - 免费     min-replicas 0 缩容到零；PostgreSQL B1ms 免费层（12 个月）
#
# 安全约定（见 docs/security/）：
#   - 签名私钥放 Key Vault（ADR-0023），identity-service 用托管身份读取；
#     密钥以 jwt-signing-<kid> 命名，轮换 = 新增一个 secret（JWKS 会同时暴露新旧公钥）。
#   - 连接串一律带 SslMode=Require。
#   - 镜像改为私有 + 配 registry 凭据（GHCR_USERNAME/GHCR_PASSWORD）。
#   - 数据库是公网可达的：本环境未接 VNet，容器应用只能从公网连库，而 ACA 出站 IP
#     不稳定，也就无法用固定 IP 白名单收窄。代价与后续处置见
#     docs/security/network-and-edge.md（P2：环境重建为 VNet 集成）。
#   - identity-service 用**独立的只读角色**（identity_service，ADR-0020），
#     口令是它自己的 secret，不用 appuser 的连接串。
#   - 部署完成后请立刻轮换数据库口令（见下方"轮换提醒"）。
#
# 顺序说明：issuer 必须等于网关的真实 FQDN，而 FQDN 只有创建网关后才知道，
# 所以脚本把网关放在最后，再回头用 az containerapp update 把 issuer 注入
# identity-service 与两个资源服务（--set-env-vars 是增量更新，不会清空其它变量）。
# ─────────────────────────────────────────────────────────────
set -euo pipefail

# ── 可调参数 ────────────────────────────────────────────────
RESOURCE_GROUP="travelMap"
ENV_NAME="cae-travelmap"
REGION="eastus"
ORG="walt-xie-hub"                        # GitHub 组织名（ghcr 命名空间）
GHCR_USERNAME="${GHCR_USERNAME:-}"        # 拉取私有 ghcr.io 镜像所需用户名
GHCR_PASSWORD="${GHCR_PASSWORD:-}"        # 拉取私有 ghcr.io 镜像所需 PAT（read:packages）
PG_SERVER="pg-travelmap"
PG_USER="appuser"
PG_DATABASE="appdb"
KV_NAME="kv-travelmap"
PG_PASSWORD="${PG_PASSWORD:?请设置 PG_PASSWORD 环境变量}"
# identity-service 的专用数据库口令（ADR-0020）。与 PG_PASSWORD 分开是有意的：
# 它对应最小权限角色，泄露这个口令拿不到 Users 的写权限。允许外部注入以复用既有环境。
IDENTITY_DB_PASSWORD="${IDENTITY_DB_PASSWORD:-$(head -c 18 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 24)}"

# 签名密钥（HS256，ADR-0005）——只用于迁移共存窗口内的旧令牌校验：
# 允许外部注入以复用既有环境的值；未注入即随机生成，避免"漏配导致服务启动即抛"。
JWT_KEY="${JWT_KEY:-$(head -c 48 /dev/urandom | base64 | tr -d '\n')}"

# 镜像拉取凭据片段（私有镜像必需；为空则不加这一段）
REGISTRY_ARGS=()
if [ -n "$GHCR_USERNAME" ] && [ -n "$GHCR_PASSWORD" ]; then
  REGISTRY_ARGS=(--registry-server ghcr.io --registry-username "$GHCR_USERNAME" --registry-password "$GHCR_PASSWORD")
else
  echo "==> 警告：未提供 GHCR_USERNAME/GHCR_PASSWORD，镜像为私有时 Container Apps 将无法拉取。"
fi

# ── 轮换提醒（详见 docs/security/secrets-and-keys.md）────────────
# 本脚本建的是公网可达的 Flexible Server。首次部署后把口令换成强随机值并同步 ACA secret：
#   NEW_PW='<新口令>'
#   az postgres flexible-server update -g travelMap -n pg-travelmap --admin-password "$NEW_PW"
#   az containerapp secret set -n user-service     -g travelMap --secrets db-password="$NEW_PW"
#   az containerapp secret set -n travel-service   -g travelMap --secrets db-password="$NEW_PW"
#   # identity-service 不跟着换 admin 口令：它用的是只读角色，单独轮换：
#   az postgres flexible-server execute -g travelMap -n pg-travelmap -d appdb \
#     -u appuser -p "$NEW_PW" -q "ALTER ROLE identity_service WITH PASSWORD '<新只读口令>';"
#   az containerapp secret set -n identity-service -g travelMap --secrets identity-db-password="<新只读口令>"
#   # 再各触发一次新 revision 让新 secret 生效
# ────────────────────────────────────────────────────────────────

# ── 1. 注册资源提供程序（新订阅首次必须）────────────────────
echo "==> 注册 Microsoft.App 资源提供程序"
az provider register --namespace Microsoft.App --wait
az provider show --namespace Microsoft.App --query registrationState -o tsv

# ── 2. 资源组 + 容器应用环境 + Key Vault ────────────────────
echo "==> 创建资源组 $RESOURCE_GROUP"
az group create --name "$RESOURCE_GROUP" --location "$REGION"

echo "==> 创建容器应用环境 $ENV_NAME"
az containerapp env create --name "$ENV_NAME" -g "$RESOURCE_GROUP" --location "$REGION"

echo "==> 创建 Key Vault $KV_NAME（签名私钥的家，ADR-0023）"
az keyvault create --name "$KV_NAME" -g "$RESOURCE_GROUP" --location "$REGION" \
  --enable-rbac-authorization false

# ── 3. PostgreSQL Flexible Server（B1ms 免费层，12 个月）────
echo "==> 创建 PostgreSQL Flexible Server $PG_SERVER（B1ms 免费层）"
# --public-access Enabled 是当前拓扑下的唯一可行选择：环境未接 VNet，
# 容器应用只能从公网连库；而 ACA 出站 IP 不稳定，无法用固定 IP 白名单收窄。
# 因此这里靠强口令 + SslMode=Require 兜住，网络层收口列在 P2。
az postgres flexible-server create -g "$RESOURCE_GROUP" \
  --name "$PG_SERVER" --sku-name Standard_B1ms --tier Burstable \
  --storage-size 32 --public-access Enabled \
  --admin-user "$PG_USER" --admin-password "$PG_PASSWORD" --yes

PG_HOST="$PG_SERVER.postgres.database.azure.com"
CONNECTION_STRING="Host=$PG_HOST;Port=5432;Database=$PG_DATABASE;Username=$PG_USER;Password=secretref:db-password;Pooling=true;SslMode=Require"
# identity-service 自己的连接串：最小权限角色 + 自己的 secret（ADR-0020）。
IDENTITY_CONNECTION_STRING="Host=$PG_HOST;Port=5432;Database=$PG_DATABASE;Username=identity_service;Password=secretref:identity-db-password;Pooling=true;SslMode=Require"

# ── 3b. identity-service 的数据库角色（ADR-0020）────────────
# 建角色 + 连库 + 建自己四张表的权限，并尝试授予 Users 的 SELECT。
# 此处库通常是空的：min-replicas 0，Users 要等 user-service 收到第一个请求才建，
# 所以传 allow_missing_users=1 降级为提示 —— user-service 启动时会以属主身份自行补授
# SELECT（见 User.Infrastructure 的 DatabaseInitializer），因此不存在启动时序问题。
echo "==> 创建 identity-service 的只读数据库角色 identity_service"
PROVISION=()
if command -v psql >/dev/null 2>&1; then
  PROVISION=(psql "host=$PG_HOST port=5432 dbname=$PG_DATABASE user=$PG_USER sslmode=require" \
    -f scripts/sql/identity-role.sql)
elif command -v docker >/dev/null 2>&1; then
  PROVISION=(docker run --rm -i -e PGPASSWORD="$PG_PASSWORD" \
    -v "$PWD/scripts/sql:/sql:ro" postgres:16-alpine \
    psql "host=$PG_HOST port=5432 dbname=$PG_DATABASE user=$PG_USER sslmode=require" \
    -f /sql/identity-role.sql)
else
  echo "!! 既没有 psql 也没有 docker，无法创建 identity_service 角色。" >&2
  echo "   请手动执行 scripts/sql/identity-role.sql，否则 identity-service 读不到 Users。" >&2
fi

if [ ${#PROVISION[@]} -gt 0 ]; then
  PGPASSWORD="$PG_PASSWORD" "${PROVISION[@]}" \
    -v role_name=identity_service \
    -v role_password="$IDENTITY_DB_PASSWORD" \
    -v allow_missing_users=1
fi

# ── 4. 四个 internal 应用 ───────────────────────────────────
# ① user-service：拥有 Users 表（含凭据列）。issuer 在网关创建后回填。
echo "==> 创建 user-service（internal）"
az containerapp create \
  --name user-service -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-user-service:latest" \
  --target-port 8080 --ingress internal --min-replicas 0 --max-replicas 3 \
  --secrets db-password="$PG_PASSWORD" jwt-key="$JWT_KEY" \
  --env-vars \
    "ConnectionStrings__DefaultConnection=$CONNECTION_STRING" \
    "Db__Password=secretref:db-password" \
    "Jwt__Key=secretref:jwt-key" \
    "Jwt__Issuer=travel-map" \
    "Jwt__Audience=travel-map-client" \
    "OTEL_TRACE_SAMPLING_RATIO=0.1" \
  "${REGISTRY_ARGS[@]}"

# ② travel-service：internal
echo "==> 创建 travel-service（internal）"
az containerapp create \
  --name travel-service -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-travel-service:latest" \
  --target-port 8080 --ingress internal --min-replicas 0 --max-replicas 3 \
  --secrets db-password="$PG_PASSWORD" jwt-key="$JWT_KEY" \
  --env-vars \
    "ConnectionStrings__DefaultConnection=$CONNECTION_STRING" \
    "Db__Password=secretref:db-password" \
    "Jwt__Key=secretref:jwt-key" \
    "Jwt__Issuer=travel-map" \
    "Jwt__Audience=travel-map-client" \
    "OTEL_TRACE_SAMPLING_RATIO=0.1" \
  "${REGISTRY_ARGS[@]}"

# ③ client：internal
echo "==> 创建 client（internal）"
az containerapp create \
  --name client -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-client:latest" \
  --target-port 80 --ingress internal --min-replicas 0 --max-replicas 3 \
  "${REGISTRY_ARGS[@]}"

# ④ identity-service：认证与令牌（ADR-0020/0021/0023）
# issuer 先写占位值，创建网关后回填真实 FQDN —— 绝不能让它变成"看起来能用但 issuer 不一致"。
echo "==> 创建 identity-service（internal，含系统分配托管身份）"
az containerapp create \
  --name identity-service -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-identity-service:latest" \
  --target-port 8080 --ingress internal --min-replicas 0 --max-replicas 3 \
  --system-assigned \
  --secrets identity-db-password="$IDENTITY_DB_PASSWORD" \
  --env-vars \
    "ConnectionStrings__DefaultConnection=$IDENTITY_CONNECTION_STRING" \
    "Identity__Issuer=https://pending.invalid/identity" \
    "Identity__ServiceId=identity-service" \
    "Identity__Audience=travel-map-client" \
    "Identity__SigningKeyStore__Provider=KeyVault" \
    "Identity__SigningKeyStore__KeyVaultUri=https://$KV_NAME.vault.azure.net/" \
    "OTEL_TRACE_SAMPLING_RATIO=0.1" \
  "${REGISTRY_ARGS[@]}"

# ── 5. 网关：external（唯一公网入口）────────────────────────
# --allow-insecure false：显式拒绝明文 HTTP（ACA 默认把 80 重定向到 443）
echo "==> 创建 gateway（external）"
az containerapp create \
  --name gateway -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-gateway:latest" \
  --target-port 80 --ingress external --allow-insecure false --min-replicas 0 --max-replicas 3 \
  --env-vars \
    "USER_SERVICE_URL=https://user-service.internal.$ENV_NAME.$REGION.azurecontainerapps.io" \
    "TRAVEL_SERVICE_URL=https://travel-service.internal.$ENV_NAME.$REGION.azurecontainerapps.io" \
    "IDENTITY_SERVICE_URL=https://identity-service.internal.$ENV_NAME.$REGION.azurecontainerapps.io" \
    "CLIENT_URL=https://client.internal.$ENV_NAME.$REGION.azurecontainerapps.io" \
  "${REGISTRY_ARGS[@]}"

GW_FQDN="$(az containerapp show -n gateway -g "$RESOURCE_GROUP" \
  --query properties.configuration.ingress.fqdn -o tsv)"
ISSUER="https://$GW_FQDN/identity"

# ── 6. 回填 issuer（必须与网关 FQDN 逐字一致）───────────────
echo "==> 回填 issuer：$ISSUER"
az containerapp update -n identity-service -g "$RESOURCE_GROUP" \
  --set-env-vars "Identity__Issuer=$ISSUER"
for app in user-service travel-service; do
  az containerapp update -n "$app" -g "$RESOURCE_GROUP" \
    --set-env-vars "Identity__Issuer=$ISSUER"
done

# ── 7. Key Vault：生成第一把签名密钥并授权给 identity-service ─
echo "==> 生成签名密钥并授予 identity-service 读取权限"
KID="$(head -c 8 /dev/urandom | od -An -tx1 | tr -d ' \n')"
KEY_FILE="$(mktemp)"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$KEY_FILE" 2>/dev/null
az keyvault secret set --vault-name "$KV_NAME" --name "jwt-signing-$KID" \
  --file "$KEY_FILE" --encoding ascii >/dev/null
rm -f "$KEY_FILE"

IDENTITY_PRINCIPAL="$(az containerapp show -n identity-service -g "$RESOURCE_GROUP" \
  --query identity.principalId -o tsv)"
az keyvault set-policy --name "$KV_NAME" \
  --object-id "$IDENTITY_PRINCIPAL" \
  --secret-permissions get list

echo "==> 完成"
echo "    网关入口（issuer 基址）：$ISSUER"
echo "    公钥集合：$ISSUER/jwks"
echo "    签名密钥：jwt-signing-$KID（轮换时新增一个 secret，JWKS 会同时暴露新旧公钥）"
echo
echo "⚠️  下一步（务必执行）："
echo "    1) 轮换数据库口令（本脚本建的是公网可达的 Flexible Server）："
echo "       NEW_PW='<强随机口令>'"
echo "       az postgres flexible-server update -g $RESOURCE_GROUP -n $PG_SERVER --admin-password \"\$NEW_PW\""
echo "       az containerapp secret set -n user-service     -g $RESOURCE_GROUP --secrets db-password=\"\$NEW_PW\""
echo "       az containerapp secret set -n travel-service   -g $RESOURCE_GROUP --secrets db-password=\"\$NEW_PW\""
echo "       az containerapp secret set -n identity-service -g $RESOURCE_GROUP --secrets db-password=\"\$NEW_PW\""
echo "    2) 把 GHCR 包改为 private（仓库 Settings → Packages），并确认上面的 registry 凭据已生效"
echo "    3) 启用生产遥测：az containerapp env telemetry app-insights set ..."
