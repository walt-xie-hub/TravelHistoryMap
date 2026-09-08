#!/bin/sh
set -e

# 高德地图配置：容器启动时由环境变量生成 runtime-config.js（运行时注入，改 key 无需重建镜像）。
# 仅当 AMAP_KEY 或 AMAP_SECURITY_JS_CODE 已设置时才写入；否则保留镜像内已有的
# runtime-config.js（例如 CI 构建时烘焙的），避免把已配置内容清空。
if [ -n "${AMAP_KEY}" ] || [ -n "${AMAP_SECURITY_JS_CODE}" ]; then
  cat > /usr/share/nginx/html/runtime-config.js <<EOF
window.__TRAVEL_MAP_CONFIG__ = {
  amapKey: '${AMAP_KEY}',
  amapSecurityJsCode: '${AMAP_SECURITY_JS_CODE}',
};
EOF
fi

exec nginx -g 'daemon off;'
