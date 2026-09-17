#!/bin/sh
# 校验 src/gateway/nginx.conf.template：渲染出真实配置并做 nginx -t 语法检查。
#
# 为什么需要它（两个已经踩过的坑）：
#   1. 裸 `envsubst` 会连 `$remote_addr`、`$http_x_forwarded_for` 一起替换成空，
#      于是 map 变成 `map  {`，报 "invalid number of arguments"。必须显式给出变量列表，
#      这也正是官方镜像 20-envsubst-on-templates.sh 的做法。
#   2. 不要用 PowerShell 的 Get-Content / Set-Content 渲染：PS 5.1 默认按 ANSI(GBK)
#      读取、写回时加 BOM，中文注释会变成乱码，nginx 会报 "unknown directive"。
#
# 用法（在仓库根目录）：
#   docker run --rm -v "$PWD:/w" nginx:1.27-alpine sh /w/scripts/validate-gateway.sh

set -e

TPL=/w/src/gateway/nginx.conf.template
OUT=/w/log/rendered-gateway.conf
TEST=/w/log/nginx-test.conf

# 用 127.0.0.1 占位而非真实 compose 服务名：nginx -t 会在加载配置时解析
# proxy_pass 里的主机名，而校验容器不在 compose 网络里（会报 host not found）。
# 这里只校验语法，不校验可达性。
export USER_SERVICE_URL=http://127.0.0.1:8080
export TRAVEL_SERVICE_URL=http://127.0.0.1:8080
export IDENTITY_SERVICE_URL=http://127.0.0.1:8080
export CLIENT_URL=http://127.0.0.1:4200

envsubst '${USER_SERVICE_URL} ${TRAVEL_SERVICE_URL} ${IDENTITY_SERVICE_URL} ${CLIENT_URL}' < "$TPL" > "$OUT"

# 模板渲染后落在 conf.d/ 下（http 上下文），这里手工补上外层包装再校验语法。
{
  echo 'events {}'
  echo 'http {'
  cat "$OUT"
  echo '}'
} > "$TEST"

nginx -t -c "$TEST"

echo '--- 认证类端点限流规则 ---'
grep -n -A2 'identity/(login' "$OUT"
grep -n -A2 'api/auth' "$OUT"
