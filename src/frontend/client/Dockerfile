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
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]

