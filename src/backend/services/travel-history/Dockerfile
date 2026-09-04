# ──────────────────────────────────────────────
# 阶段 1：BUILD — 用 SDK 镜像编译 & publish
# 构建上下文 = src/backend（包含 services/travel-history 与 shared/ 两个目录），
# 因此跨服务共享项目的 ProjectReference（..\..\..\..\shared\...）在容器内可正确解析。
# ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# ① 先只复制解决方案与所有 .csproj（含跨服务共享库）—— 利用 Docker 层缓存
#    只要依赖没变，这一层就不会重建，restore 不用重跑
COPY services/travel-history/travel-history.slnx ./services/travel-history/
COPY services/travel-history/src/Travel.Domain/Travel.Domain.csproj ./services/travel-history/src/Travel.Domain/
COPY services/travel-history/src/Travel.Application/Travel.Application.csproj ./services/travel-history/src/Travel.Application/
COPY services/travel-history/src/Travel.Infrastructure/Travel.Infrastructure.csproj ./services/travel-history/src/Travel.Infrastructure/
COPY services/travel-history/src/Travel.Api/travel.csproj ./services/travel-history/src/Travel.Api/
COPY services/travel-history/tests/Travel.UnitTests/Travel.UnitTests.csproj ./services/travel-history/tests/Travel.UnitTests/
COPY shared/Shared.Contracts/Shared.Contracts.csproj ./shared/Shared.Contracts/
COPY shared/Shared.Observability/Shared.Observability.csproj ./shared/Shared.Observability/

RUN dotnet restore services/travel-history/travel-history.slnx

# ② 复制剩余源码，先跑单元测试再 publish（Release → /app/out）
#    测试失败会中断镜像构建 —— CI 只跑 docker build 即可完成全部验证
COPY . .
RUN dotnet test services/travel-history/travel-history.slnx -c Release --no-restore
RUN dotnet publish services/travel-history/src/Travel.Api/travel.csproj -c Release -o /app/out --no-restore

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

ENTRYPOINT ["dotnet", "travel.dll"]
