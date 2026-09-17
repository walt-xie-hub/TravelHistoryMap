# ──────────────────────────────────────────────
# 阶段 1：BUILD — 用 SDK 镜像编译 & publish
# 构建上下文 = src/backend（包含 services/identity-service 与 shared/ 两个目录），
# 因此跨服务共享项目的 ProjectReference（..\..\..\..\shared\...）在容器内可正确解析。
# ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# ① 先只复制解决方案与所有 .csproj（含跨服务共享库）—— 利用 Docker 层缓存
COPY services/identity-service/identity.slnx ./services/identity-service/
COPY services/identity-service/src/Identity.Domain/Identity.Domain.csproj ./services/identity-service/src/Identity.Domain/
COPY services/identity-service/src/Identity.Application/Identity.Application.csproj ./services/identity-service/src/Identity.Application/
COPY services/identity-service/src/Identity.Infrastructure/Identity.Infrastructure.csproj ./services/identity-service/src/Identity.Infrastructure/
COPY services/identity-service/src/Identity.Api/identity.csproj ./services/identity-service/src/Identity.Api/
COPY services/identity-service/tests/Identity.UnitTests/Identity.UnitTests.csproj ./services/identity-service/tests/Identity.UnitTests/
COPY shared/Shared.Contracts/Shared.Contracts.csproj ./shared/Shared.Contracts/
COPY shared/Shared.Observability/Shared.Observability.csproj ./shared/Shared.Observability/

RUN dotnet restore services/identity-service/identity.slnx

# ② 复制剩余源码，先跑单元测试再 publish（Release → /app/out）
#    测试失败会中断镜像构建 —— CI 只跑 docker build 即可完成全部验证
COPY . .
RUN dotnet test services/identity-service/identity.slnx -c Release --no-restore
RUN dotnet publish services/identity-service/src/Identity.Api/identity.csproj -c Release -o /app/out --no-restore

# ──────────────────────────────────────────────
# 阶段 2：RUNTIME — 只用轻量的 aspnet 运行时镜像
# ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/out ./

# 安装 Npgsql 在 Linux 上做 GSSAPI/Kerberos 所需的 libgssapi，
# 否则首次连接会刷 "Cannot load library libgssapi_krb5.so.2" 警告日志。
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# 安全最佳实践：不以 root 运行
USER app

ENV DOTNET_USE_POLLING_FILE_WATCHER=true
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "identity.dll"]
