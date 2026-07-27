# ──────────────────────────────────────────────
# 阶段 1：BUILD — 用 SDK 镜像编译 & publish
# 构建上下文 = src/backend（包含 services/user-service 与 shared/ 两个目录），
# 因此跨服务共享项目的 ProjectReference（..\..\..\..\shared\...）在容器内可正确解析。
# ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# ① 先只复制解决方案与所有 .csproj（含跨服务共享库）—— 利用 Docker 层缓存
#    只要依赖没变，这一层就不会重建，restore 不用重跑
COPY services/user-service/user.slnx ./services/user-service/
COPY services/user-service/src/User.Domain/User.Domain.csproj ./services/user-service/src/User.Domain/
COPY services/user-service/src/User.Application/User.Application.csproj ./services/user-service/src/User.Application/
COPY services/user-service/src/User.Infrastructure/User.Infrastructure.csproj ./services/user-service/src/User.Infrastructure/
COPY services/user-service/src/User.Api/user.csproj ./services/user-service/src/User.Api/
COPY services/user-service/tests/User.UnitTests/User.UnitTests.csproj ./services/user-service/tests/User.UnitTests/
COPY shared/Shared.Contracts/Shared.Contracts.csproj ./shared/Shared.Contracts/
COPY shared/Shared.Observability/Shared.Observability.csproj ./shared/Shared.Observability/

RUN dotnet restore services/user-service/user.slnx

# ② 复制剩余源码并 publish（Release → /app/out）
COPY . .
RUN dotnet publish services/user-service/src/User.Api/user.csproj -c Release -o /app/out --no-restore

# ──────────────────────────────────────────────
# 阶段 2：RUNTIME — 只用轻量的 aspnet 运行时镜像
# ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

# .NET 10 的 aspnet 镜像里自带一个非特权用户 "app"
# 把文件权限交给它，然后切身份
COPY --from=build /app/out ./

# 安装 Npgsql 在 Linux 上做 GSSAPI/Kerberos 所需的 libgssapi，
# 否则首次连接会刷 "Cannot load library libgssapi_krb5.so.2" 警告日志。
# 用完即删 apt 缓存，保持镜像尽量小。
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# 安全最佳实践：不以 root 运行
# （aspnet:10.0 镜像里已有 USER app 的设置，但显式设一下更稳）
USER app

ENV DOTNET_USE_POLLING_FILE_WATCHER=true
ENV ASPNETCORE_URLS=http://+:8080

# ASP.NET Core 在容器里默认监听 http://+:8080
# 告诉 Docker 这个镜像"声明"要用 8080
EXPOSE 8080 8081

ENTRYPOINT ["dotnet", "user.dll"]
