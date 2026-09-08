FROM node:24-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build -- --configuration production
# 单元测试（vitest）：测试失败会中断镜像构建 —— CI 只跑 docker build 即可完成全部验证
RUN npx ng test --watch=false

FROM nginx:alpine
COPY --from=build /app/dist/client/browser /usr/share/nginx/html
# SPA history 回退（/login、/home 等深链直接访问/刷新返回 index.html，而非 404）
COPY nginx.conf /etc/nginx/conf.d/default.conf
# 启动时用 AMAP_KEY / AMAP_SECURITY_JS_CODE 环境变量生成 runtime-config.js（运行时注入高德配置）
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh
EXPOSE 80
ENTRYPOINT ["/docker-entrypoint.sh"]

