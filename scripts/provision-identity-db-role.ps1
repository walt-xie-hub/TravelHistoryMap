#requires -Version 5.1
<#
.SYNOPSIS
    本地 compose：为 identity-service 创建最小权限数据库角色（ADR-0020）。

.DESCRIPTION
    执行 scripts/sql/identity-role.sql，把「identity-service 永远不得获得
    Users 写权限」从约定变成 PostgreSQL 权限系统里的事实。

    本机不需要装 psql：库跑在容器里，脚本把 SQL **拷进**容器后执行。
    用 docker cp 而不是管道，是因为 PowerShell 5.1 的管道会按 $OutputEncoding
    （默认 ASCII）转码，中文注释与提示文本会被打成问号。

    幂等：可反复执行。口令每次都会被对齐成 .env 里的值。

.PARAMETER RoleName
    角色名，默认 identity_service。必须与 Identity__ConnectionStrings 里的 Username 一致。

.PARAMETER RolePassword
    角色口令。省略时从仓库根目录的 .env 读取 IDENTITY_DB_PASSWORD。

.EXAMPLE
    pwsh -File scripts/provision-identity-db-role.ps1

.NOTES
    前置：docker compose up -d db server 已启动，且 user-service 至少启动过一次
    （它是 Users 的属主，表由它建；本脚本在表缺失时会直接报错）。
#>
[CmdletBinding()]
param(
    [string]$RoleName = 'identity_service',
    [string]$RolePassword,
    [string]$Container = 'postgres-db',
    [string]$Database = 'appdb',
    [string]$Owner = 'appuser'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sqlFile = Join-Path $root 'scripts\sql\identity-role.sql'

if (-not $RolePassword) {
    $envFile = Join-Path $root '.env'
    if (Test-Path $envFile) {
        $match = Select-String -Path $envFile -Pattern '^IDENTITY_DB_PASSWORD=(.+)$' | Select-Object -First 1
        if ($match) {
            $RolePassword = $match.Matches[0].Groups[1].Value.Trim()
        }
    }
}

if (-not $RolePassword) {
    throw '缺少角色口令：请在仓库根目录的 .env 里设置 IDENTITY_DB_PASSWORD，或用 -RolePassword 传入。'
}

if (-not (Test-Path $sqlFile)) {
    throw "找不到 SQL 文件：$sqlFile"
}

$running = docker ps --filter "name=^/$Container$" --format '{{.Names}}'
if (-not $running) {
    throw "容器 $Container 没在运行：先执行 docker compose up -d db server"
}

# 拷进去再执行：字节原样，且不把 SQL 内容塞进命令行
$staged = '/tmp/identity-role.sql'
docker cp $sqlFile "${Container}:$staged"
if ($LASTEXITCODE -ne 0) {
    throw '把 SQL 拷进容器失败。'
}

# 口令经 -v 传入（会出现在本机进程列表里）。本地开发可接受；
# 生产不走这个脚本，见 deploy-azure.sh。执行者必须是库属主：Users 的属主是 appuser。
docker exec $Container psql -U $Owner -d $Database `
    -v role_name="$RoleName" `
    -v role_password="$RolePassword" `
    -f $staged
if ($LASTEXITCODE -ne 0) {
    throw "授权脚本执行失败（退出码 $LASTEXITCODE）。"
}

docker exec $Container rm -f $staged | Out-Null

Write-Host ''
Write-Host "完成：角色 $RoleName 已就绪，对 Users 只有 SELECT（ADR-0020）。"
Write-Host 'identity-service 用这个角色连接；若它已在运行，重启一次让它换上新连接串。'
