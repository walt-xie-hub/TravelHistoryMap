#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────
# Azure Container Apps 一次性初始化脚本
# 用途：创建资源组 / 容器应用环境 / PostgreSQL 免费层 / 3 个 Container App
# 前置：已登录 az（Cloud Shell 或本地 az CLI），且已注册 Microsoft.App
# 使用：PG_PASSWORD="你的密码" bash deploy-azure.sh
#
# 与 .github/workflows/ci.yml 的约定：
#   - 资源组   rg-travelmap
#   - 环境     cae-travelmap（区域 eastasia，与 CAE_NAME/CAE_REGION 一致）
#   - 镜像     ghcr.io/walt-xie-hub/travelmap-{client,user-service,gateway}
#   - ingress  client/user-service 为 internal，gateway 为 external（唯一入口）
#   - 免费     min-replicas 0 缩容到零；PostgreSQL B1ms 免费层（12 个月）
# ─────────────────────────────────────────────────────────────
set -euo pipefail

# ── 可调参数 ────────────────────────────────────────────────
RESOURCE_GROUP="rg-travelmap"
ENV_NAME="cae-travelmap"
REGION="eastasia"
ORG="walt-xie-hub"                        # GitHub 组织名（ghcr 命名空间）
PG_SERVER="pg-travelmap"
PG_USER="appuser"
PG_DATABASE="appdb"
PG_PASSWORD="${PG_PASSWORD:?请设置 PG_PASSWORD 环境变量}"

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
az postgres flexible-server create -g "$RESOURCE_GROUP" \
  --name "$PG_SERVER" --sku-name Standard_B1ms --tier Burstable \
  --storage-size 32 --public-access Enabled \
  --admin-user "$PG_USER" --admin-password "$PG_PASSWORD" --yes

PG_HOST="$PG_SERVER.postgres.database.azure.com"

# ── 4. 三个 Container App ───────────────────────────────────
# ① 后端 user-service：internal，连接串用 secretref 引用 db-password
echo "==> 创建 user-service（internal）"
az containerapp create \
  --name user-service -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-user-service:latest" \
  --target-port 8080 --ingress internal --min-replicas 0 --max-replicas 3 \
  --secrets db-password="$PG_PASSWORD" \
  --env-vars \
    "ConnectionStrings__DefaultConnection=Host=$PG_HOST;Port=5432;Database=$PG_DATABASE;Username=$PG_USER;Password=secretref:db-password;Pooling=true" \
    "Db__Password=secretref:db-password"

# ② 前端 client：internal
echo "==> 创建 client（internal）"
az containerapp create \
  --name client -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-client:latest" \
  --target-port 80 --ingress internal --min-replicas 0 --max-replicas 3

# ③ 网关 gateway：external（唯一公网入口），转发到两个 internal 服务
echo "==> 创建 gateway（external）"
az containerapp create \
  --name gateway -g "$RESOURCE_GROUP" --environment "$ENV_NAME" \
  --image "ghcr.io/$ORG/travelmap-gateway:latest" \
  --target-port 80 --ingress external --min-replicas 0 --max-replicas 3 \
  --env-vars \
    "USER_SERVICE_URL=http://user-service.internal.$ENV_NAME.$REGION.azurecontainerapps.io" \
    "CLIENT_URL=http://client.internal.$ENV_NAME.$REGION.azurecontainerapps.io"

# ── 5. 输出公网入口 ─────────────────────────────────────────
echo "==> 部署完成，网关入口："
az containerapp show -n gateway -g "$RESOURCE_GROUP" \
  --query properties.configuration.ingress.fqdn -o tsv
