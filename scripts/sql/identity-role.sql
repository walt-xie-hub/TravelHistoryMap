-- ─────────────────────────────────────────────────────────────
-- identity-service 的数据库角色：只读 Users，绝不写它（ADR-0020）
--
-- 用途：把「identity-service 永远不得获得 Users 的写权限」从**约定**变成权限系统里的
--       **事实**。此前它与 user-service 共用 appuser，实际上能 UPDATE / INSERT / DELETE
--       Users；换成这个角色之后，这类语句会被 PostgreSQL 直接拒绝。
--
-- 幂等：可重复执行。已存在的角色会被重新对齐口令与属性（防止有人手工放宽过）。
--
-- 调用方（必须以库属主 / 管理员身份执行）：
--   psql -v role_name=identity_service -v role_password=... -f scripts/sql/identity-role.sql
--   scripts/provision-identity-db-role.ps1     # 本地 compose
--   deploy-azure.sh                            # Azure
--
-- 授予的全部权限就是这些：
--   CONNECT on database —— 能连
--   USAGE   on schema   —— 能看见 public 下的对象
--   CREATE  on schema   —— 能建自己的四张表（RefreshTokens / ServiceClients /
--                          ServiceClientScopes / LoginAttempts）
--   SELECT  on "Users"  —— 只读认证所需的最小列，由代码保证（NpgsqlCredentialReader）
--
-- 刻意**不给**：Users 的 INSERT / UPDATE / DELETE / TRUNCATE、CREATEDB、SUPERUSER、
-- BYPASSRLS。改或删 user-service 的表需要属主身份，schema 上的 CREATE 权限给不了这个能力。
-- ─────────────────────────────────────────────────────────────

\set ON_ERROR_STOP on

\if :{?role_name}
\else
\echo '错误：缺少 -v role_name=...'
\quit
\endif

\if :{?role_password}
\else
\echo '错误：缺少 -v role_password=...'
\quit
\endif

-- 1) 建角色：非超级用户、不能建库、不能建角色、不能绕过行级安全
SELECT format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
              :'role_name', :'role_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'role_name')
\gexec

-- 2) 已存在则对齐口令，并把上面那组属性重新钉死
SELECT format('ALTER ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
              :'role_name', :'role_password')
WHERE EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'role_name')
\gexec

-- 3) 连库 + 使用 public schema + 建自己表的权限
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), :'role_name')
\gexec

SELECT format('GRANT USAGE, CREATE ON SCHEMA public TO %I', :'role_name')
\gexec

-- 4) Users 只读（本文件的重点）。表还不存在时静默跳过，由第 6 步统一报错。
SELECT format('GRANT SELECT ON TABLE public."Users" TO %I', :'role_name')
WHERE to_regclass('public."Users"') IS NOT NULL
\gexec

-- 5) 兜底：identity-service 自己的四张表若已被别的角色建过（例如早期用 appuser 跑过一次），
--    把使用权交给它。正常路径下这些表由它自己建，因此不会命中。
SELECT format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.%I TO %I', t, :'role_name')
FROM unnest(ARRAY['RefreshTokens', 'ServiceClients', 'ServiceClientScopes', 'LoginAttempts']) AS t
WHERE to_regclass(format('public.%I', t)) IS NOT NULL
\gexec

-- 6) Users 不存在时的行为。默认**报错退出**：本地开发与修现网时宁可现在失败，
--    也不要等 identity-service 起来才发现它读不到凭据。
--    Azure 首次部署是例外：库是空的，而 Users 要等 user-service 收到第一个请求才会建
--    （min-replicas 0 缩容到零）。那种情况传 -v allow_missing_users=1 降级为提示 ——
--    user-service 启动时会自行把 SELECT 授予本角色（它是 Users 的属主，
--    见 User.Infrastructure 的 DatabaseInitializer），所以不存在时序问题。
\if :{?allow_missing_users}
  \echo '注意：allow_missing_users 已设置 —— Users 不存在时只提示，不报错。'
  SELECT format('DO $do$ BEGIN RAISE NOTICE %L; END $do$',
                'public."Users" 尚未创建：user-service 首次启动时会建表并把 SELECT 授予本角色。')
  WHERE to_regclass('public."Users"') IS NULL
  \gexec
\else
  SELECT format('DO $do$ BEGIN RAISE EXCEPTION %L; END $do$',
                'public."Users" 不存在：请先启动 user-service 建立该表，再重跑本脚本。')
  WHERE to_regclass('public."Users"') IS NULL
  \gexec
\endif

-- 7) 打印实际授权，便于人工核对：应当只有 SELECT 一行
SELECT privilege_type
FROM information_schema.role_table_grants
WHERE table_name = 'Users' AND grantee = :'role_name'
ORDER BY 1;
