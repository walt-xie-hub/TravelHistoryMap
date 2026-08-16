# ──────────────────────────────────────────────
# 网关容器：在 Azure Container Apps 里复刻 K8s 的 nginx Ingress 角色
# 唯一外部入口，按路径分流：
#   /api/*  → user-service（后端）
#   /*      → client（前端静态站）
# 配置通过环境变量 USER_SERVICE_URL / CLIENT_URL 注入，
# 因此镜像无需在构建期写死后端地址。
#
# nginx 官方镜像会自动处理 /etc/nginx/templates/*.template：
# 启动时用 envsubst 替换其中已定义的环境变量，输出到 /etc/nginx/conf.d/
# （未定义的 $host 等 nginx 内置变量不受影响）
# ──────────────────────────────────────────────
FROM nginx:alpine
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
EXPOSE 80
