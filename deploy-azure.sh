#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────
# Azure Container Apps 一次性初始化脚本
# 用途：创建资源组 / 容器应用环境 / PostgreSQL 免费层 / 4 个 Container App
# 前置：已登录 az（Cloud Shell 或本地 az CLI），且已注册 Microsoft.App
# 使用：PG_PASSWORD="你的密码" bash deploy-azure.sh
#
# 与 .github/workflows/ci.yml 的约定：
#   - 资源组   travelMap
#   - 环境     cae-travelmap（区域 eastus，与 CAE_NAME/CAE_REGION 一致）
#   - 镜像     ghcr.io/walt-xie-hub/travelmap-{client,user-service,travel-service,gateway}
#   - ingress  client/user-service/travel-service 为 internal，gateway 为 external（唯一入口）
#   - 免费     min-replicas 0 缩容到零；PostgreSQL B1ms 免费层（12 个月）
#
# 安全约定（见 docs/security/）：
#   - 不再要求"在 ACA 上手工配置签名密钥"：JWT_KEY 未注入时随机生成。
#   - 连接串一律带 SslMode=Require（托管库支持 TLS）。
#   - 数据库是公网可达的：本环境未接入 VNet，容器应用只能从公网连库，而 ACA 出站 IP
#     不稳定，也就无法用固定 IP 白名单收窄。代价与后续处置见
#     docs/security/network-and-edge.md（P2：环境重建为 VNet 集成）。
#   - 部署完成后请立刻轮换数据库口令（见下方"轮换提醒"）。
# ─────────────────────────────────────────────────────────────
set -euo pipefail

# ── 可调参数 ────────────────────────────────────────────────
RESOURCE_GROUP="travelMap"
ENV_NAME="cae-travelmap"
REGION="eastus"
ORG="walt-xie-hub"                        # GitHub 组织名（ghcr 命名空间）
GHCR_USERNAME="${GHCR_USERNAME:-}"        # 拉取私有 ghcr.io 镜像所需用户名
GHCR_PASSWORD="${GHCR_PASSWORD:-}"        # 拉取私有 ghcr.io 镜像所需 PAT
PG_SERVER="pg-travelmap"
PG_USER="appuser"
PG_DATABASE="appdb"
PG_PASSWORD="${PG_PASSWORD:?请设置 PG_PASSWORD 环境变量}"

# 签名密钥（HS256，ADR-0005）：允许外部注入以复用既有环境的值；
# 未注入即随机生成，避免"漏配导致服务启动即抛 Jwt:Key is not configured"。
# 48 字节 → base64，去掉换行。
JWT_KEY="${JWT_KEY:-$(head -c 48 /dev/urandom | base64 | tr -d '\n')}"

# ── 轮换提醒（详见 docs/security/secrets-and-keys.md）────────────
# 本脚本建的是公网可达的 Flexible Server，而历史仓库里出现过弱口令。
# 首次部署后必须立刻把口令换成强随机值，并同步 ACA 的 secret：
#   NEW_PW='<新口令>'
#   az postgres flexible-server update -g travelMap -n pg-travelmap --admin-password "$NEW_PW"
#   az containerapp secret set -n user-service   -g travelMap --secrets db-password="$NEW_PW"
#   az containerapp secret set -n travel-service -g travelMap --secrets db-password="$NEW_PW"
#   # 再各触发一次新 revision 让新 secret 生效
# ────────────────────────────────────────────────────────────────

# ── 1. 注册资源提供程序（新订阅首次必须）────────────────────
echo "==> 注册 Microsoft.App 资源提供程序"
az provider register --namespace Microsoft.App --wait
az provider show --namespace Microsoft.App --query registrationState -o tsv

# ── 2. 资源组 + 容器应用环境 ────────────────────────────────
echo "==> 创建资源组 $RESOURCE_GROUP"
az group create --name "$RESOURCE_GROUP" --location "$REGION"

echo "==> 创建容器应用环境 $ENV_NAME"
az containerapp env create --name "$ENV_NAME" -g "$RESOURCE_GROUP" --location "$REGION"

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

# ── 4. 四个 Container App ───────────────────────────────────
# ① 后端 user-service：internal，连接串与签名密钥都用 secretref 引用
echo "==> 创建 user-service（internal）"
az containerapp create \
  --name user-service -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-user-service:latest" \
  --target-port 8080 --ingress internal --min-replicas 0 --max-replicas 3 \
  --secrets db-password="$PG_PASSWORD" jwt-key="$JWT_KEY" \
  --env-vars \
    "ConnectionStrings__DefaultConnection=Host=$PG_HOST;Port=5432;Database=$PG_DATABASE;Username=$PG_USER;Password=secretref:db-password;Pooling=true;SslMode=Require" \
    "Db__Password=secretref:db-password" \
    "Jwt__Key=secretref:jwt-key" \
    "Jwt__Issuer=travel-map" \
    "Jwt__Audience=travel-map-client" \
    "OTEL_TRACE_SAMPLING_RATIO=0.1" \
  $( [ -n "$GHCR_USERNAME" ] && echo "--registry-server ghcr.io --registry-username $GHCR_USERNAME --registry-password $GHCR_PASSWORD" )

# ② travel-service：internal
echo "==> 创建 travel-service（internal）"
az containerapp create \
  --name travel-service -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-travel-service:latest" \
  --target-port 8080 --ingress internal --min-replicas 0 --max-replicas 3 \
  --secrets db-password="$PG_PASSWORD" jwt-key="$JWT_KEY" \
  --env-vars \
    "ConnectionStrings__DefaultConnection=Host=$PG_HOST;Port=5432;Database=$PG_DATABASE;Username=$PG_USER;Password=secretref:db-password;Pooling=true;SslMode=Require" \
    "Db__Password=secretref:db-password" \
    "Jwt__Key=secretref:jwt-key" \
    "Jwt__Issuer=travel-map" \
    "Jwt__Audience=travel-map-client" \
    "OTEL_TRACE_SAMPLING_RATIO=0.1" \
  $( [ -n "$GHCR_USERNAME" ] && echo "--registry-server ghcr.io --registry-username $GHCR_USERNAME --registry-password $GHCR_PASSWORD" )

# ③ 前端 client：internal
echo "==> 创建 client（internal）"
az containerapp create \
  --name client -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-client:latest" \
  --target-port 80 --ingress internal --min-replicas 0 --max-replicas 3 \
  $( [ -n "$GHCR_USERNAME" ] && echo "--registry-server ghcr.io --registry-username $GHCR_USERNAME --registry-password $GHCR_PASSWORD" )

# ④ 网关 gateway：external（唯一公网入口），转发到三个 internal 服务
# --allow-insecure false：显式拒绝明文 HTTP（ACA 默认会把 80 重定向到 443，
# 这里把"只走 HTTPS"写成部署时的显式约定，而不是依赖平台默认值）。
echo "==> 创建 gateway（external）"
az containerapp create \
  --name gateway -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-gateway:latest" \
  --target-port 80 --ingress external --allow-insecure false --min-replicas 0 --max-replicas 3 \
  --env-vars \
    "USER_SERVICE_URL=https://user-service.internal.$ENV_NAME.$REGION.azurecontainerapps.io" \
    "TRAVEL_SERVICE_URL=https://travel-service.internal.$ENV_NAME.$REGION.azurecontainerapps.io" \
    "CLIENT_URL=https://client.internal.$ENV_NAME.$REGION.azurecontainerapps.io" \
  $( [ -n "$GHCR_USERNAME" ] && echo "--registry-server ghcr.io --registry-username $GHCR_USERNAME --registry-password $GHCR_PASSWORD" )

# ── 5. 输出公网入口 ─────────────────────────────────────────
echo "==> 部署完成，网关入口："
az containerapp show -n gateway -g "$RESOURCE_GROUP" \
  --query properties.configuration.ingress.fqdn -o tsv
