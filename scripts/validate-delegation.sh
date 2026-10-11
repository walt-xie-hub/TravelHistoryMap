#!/usr/bin/env bash
#
# 验收「用户委托（token exchange）」与首个真实跨服务调用（ADR-0024 / ADR-0025）。
#
# 它回答的问题：服务身份与用户委托这两套机制**在一个真实业务端点上**是否真的按预期裁决。
# 单测覆盖不到线上那一跳（要真起 identity + 两个服务），所以这里用 curl 把整条链路走一遍，
# 每一步断言一个**期望的失败**或**期望的成功** —— 全部通过才说明链路是通的、且边界是紧闭的。
#
# 前置：本地 compose 栈已起（identity :8090 / user-service :8080 / travel-server :8081），
#       且 .env 里的 client secret 已注入（与 docker-compose.dev.yml 同一份）。
#
# 需要 curl 与 jq。登录有图片验证码，脚本不代你登录 ——
# 请从浏览器 DevTools 取当前用户的 access token：
#     localStorage['travel-map.auth.token']
#
# 用法（仓库根目录）：
#   SUBJECT_TOKEN='<粘贴 access token>' \
#   USER_SERVICE_CLIENT_SECRET="$(grep '^USER_SERVICE_CLIENT_SECRET=' .env | cut -d= -f2-)" \
#   bash scripts/validate-delegation.sh
#
# 可选覆盖：IDENTITY_BASE / USER_API / TRAVEL_BASE / USER_CLIENT_ID / AUDIENCE / SCOPE
#
# 默认用 127.0.0.1 而不是 localhost：dev 栈的宿主端口只绑了 IPv4（127.0.0.1:xxxx:xxxx），
# 而部分环境把 localhost 先解析成 ::1，会出现“服务明明是好的、curl 却连不上”的奇观。

set -euo pipefail

IDENTITY_BASE="${IDENTITY_BASE:-http://127.0.0.1:8090/identity}"
USER_API="${USER_API:-http://127.0.0.1:8080/api}"
TRAVEL_BASE="${TRAVEL_BASE:-http://127.0.0.1:8081}"
USER_CLIENT_ID="${USER_CLIENT_ID:-user-service}"
AUDIENCE="${AUDIENCE:-travel-service}"
SCOPE="${SCOPE:-travel:read}"
# 一个**故意没被授予**的 scope：用来证明"scope 必须已授予"这道闸门是关着的
UNGRANTED_SCOPE="${UNGRANTED_SCOPE:-user:read}"

: "${USER_SERVICE_CLIENT_SECRET:?请提供 USER_SERVICE_CLIENT_SECRET（见文件头用法）}"
: "${SUBJECT_TOKEN:?请提供 SUBJECT_TOKEN（当前用户的 access token，见文件头用法）}"

command -v curl >/dev/null || { echo '需要 curl。' >&2; exit 2; }
command -v jq   >/dev/null || { echo '需要 jq。' >&2; exit 2; }

PASSED=0
FAILED=0

ok() { PASSED=$((PASSED + 1)); printf '  [PASS] %s\n' "$1"; }
no() { FAILED=$((FAILED + 1)); printf '  [FAIL] %s（期望 %s，实际 %s）\n' "$1" "$2" "$3"; }

# 断言 HTTP 状态码
expect_status() { # expect_status <描述> <期望码> <实际码>
  if [ "$2" = "$3" ]; then ok "$1"; else no "$1" "$2" "$3"; fi
}

# 断言字符串相等（用于 OAuth error 之类的精确值）
expect_eq() { # expect_eq <描述> <期望> <实际>
  if [ "$2" = "$3" ]; then ok "$1"; else no "$1" "$2" "$3"; fi
}

status_of() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

echo '== 0. 环境检查 =='
expect_status 'identity 的 OIDC 发现可达' 200 \
  "$(status_of "$IDENTITY_BASE/.well-known/openid-configuration")"
expect_status 'travel-service 健康' 200 "$(status_of "$TRAVEL_BASE/health")"
expect_status 'user-service 健康' 200 "$(status_of "${USER_API%/api}/health")"

echo
echo '== 1. 内部端点必须拒绝不认识的身份（ADR-0019/0022）=='
expect_status '无令牌调 /internal/whoami → 401' 401 \
  "$(status_of "$TRAVEL_BASE/internal/whoami")"

expect_status '用户令牌调 /internal/whoami → 401（aud 是客户端标识，与 Delegated 方案不匹配）' 401 \
  "$(status_of -H "Authorization: Bearer $SUBJECT_TOKEN" "$TRAVEL_BASE/internal/whoami")"

# client credentials：拿到"纯服务身份"令牌（没有 scope 也允许，这是它与委托的关键差别之一）
SERVICE_TOKEN_RESPONSE="$(curl -s -X POST "$IDENTITY_BASE/token" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode 'grant_type=client_credentials' \
  --data-urlencode "client_id=$USER_CLIENT_ID" \
  --data-urlencode "client_secret=$USER_SERVICE_CLIENT_SECRET" \
  --data-urlencode "audience=$AUDIENCE" \
  --data-urlencode 'scope=')"
SERVICE_TOKEN="$(printf '%s' "$SERVICE_TOKEN_RESPONSE" | jq -r '.access_token // empty')"
if [ -n "$SERVICE_TOKEN" ]; then ok 'client_credentials 换到服务令牌'; else no 'client_credentials 换到服务令牌' 'token' "$SERVICE_TOKEN_RESPONSE"; fi

expect_status '服务令牌调 /internal/whoami → 403（aud 对但缺 act：服务令牌冒充不了用户委托）' 403 \
  "$(status_of -H "Authorization: Bearer $SERVICE_TOKEN" "$TRAVEL_BASE/internal/whoami")"

echo
echo '== 2. token exchange 的四道闸门（默认拒绝，ADR-0024）=='
exchange() { # exchange <audience> <scope> [client_secret] → 输出响应体
  local audience="$1" scope="$2" secret="${3:-$USER_SERVICE_CLIENT_SECRET}"
  curl -s -X POST "$IDENTITY_BASE/token" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode 'grant_type=urn:ietf:params:oauth:grant-type:token-exchange' \
    --data-urlencode "client_id=$USER_CLIENT_ID" \
    --data-urlencode "client_secret=$secret" \
    --data-urlencode "subject_token=$SUBJECT_TOKEN" \
    --data-urlencode 'subject_token_type=urn:ietf:params:oauth:token-type:access_token' \
    --data-urlencode "audience=$audience" \
    --data-urlencode "scope=$scope"
}

expect_eq '错误 secret → invalid_client（先验调用方，未认证请求没机会试探主体令牌）' invalid_client \
  "$(exchange "$AUDIENCE" "$SCOPE" 'definitely-not-the-secret' | jq -r '.error // empty')"

expect_eq 'audience 不在白名单 → invalid_target' invalid_target \
  "$(exchange 'not-a-registered-service' "$SCOPE" | jq -r '.error // empty')"

expect_eq '缺 scope → invalid_scope（否则委托令牌退化成"用户令牌的副本"）' invalid_scope \
  "$(exchange "$AUDIENCE" '' | jq -r '.error // empty')"

expect_eq "未授予的 scope（$UNGRANTED_SCOPE）→ invalid_scope" invalid_scope \
  "$(exchange "$AUDIENCE" "$UNGRANTED_SCOPE" | jq -r '.error // empty')"

echo
echo '== 3. 换到委托令牌，并核对它的形状 =='
DELEGATED_RESPONSE="$(exchange "$AUDIENCE" "$SCOPE")"
DELEGATED_TOKEN="$(printf '%s' "$DELEGATED_RESPONSE" | jq -r '.access_token // empty')"
if [ -n "$DELEGATED_TOKEN" ]; then
  ok '换取委托令牌成功'
else
  no '换取委托令牌成功' 'access_token' "$DELEGATED_RESPONSE"
  echo; echo "致命：拿不到委托令牌，后续断言无意义。"; exit 1
fi

TTL="$(printf '%s' "$DELEGATED_RESPONSE" | jq -r '.expires_in // 0')"
if [ "$TTL" -le 300 ] && [ "$TTL" -gt 0 ]; then
  ok "TTL 不超过服务令牌的 5 分钟（expires_in=$TTL）"
else
  no 'TTL 不超过服务令牌的 5 分钟' '≤300' "$TTL"
fi

WHOAMI="$(curl -s -H "Authorization: Bearer $DELEGATED_TOKEN" "$TRAVEL_BASE/internal/whoami")"
ACTING="$(printf '%s' "$WHOAMI" | jq -r '.actingService // empty')"
SUBJECT="$(printf '%s' "$WHOAMI" | jq -r '.subject // empty')"
expect_eq 'whoami 回显代理方 = service:user-service' "service:$USER_CLIENT_ID" "$ACTING"
if printf '%s' "$SUBJECT" | grep -Eq '^[0-9]+$'; then
  ok "whoami 回显主体是十进制用户 id（subject=$SUBJECT）"
else
  no 'whoami 回显主体是十进制用户 id' '十进制' "$SUBJECT"
fi

echo
echo '== 4. 首个真实业务端点（ADR-0025）=='
INTERNAL_BODY="$(curl -s -H "Authorization: Bearer $DELEGATED_TOKEN" "$TRAVEL_BASE/internal/travels/footprint")"
if printf '%s' "$INTERNAL_BODY" | jq -e 'has("totalRecords") and has("cityCount") and has("favoriteCount")' >/dev/null; then
  ok "委托令牌读内部端点成功：$(printf '%s' "$INTERNAL_BODY" | jq -c '{totalRecords, cityCount, favoriteCount}')"
else
  no '委托令牌读内部端点成功' 'FootprintOverview' "$INTERNAL_BODY"
fi

expect_status '委托令牌不能当用户令牌用（aud 不匹配用户方案）→ 401' 401 \
  "$(status_of -H "Authorization: Bearer $DELEGATED_TOKEN" "${USER_API%/api}/api/travels")"

# 端到端：浏览器只调 user-service，跨服务那一跳由它自己完成
USER_BODY="$(curl -s -H "Authorization: Bearer $SUBJECT_TOKEN" "$USER_API/users/me/footprint")"
if [ "$(printf '%s' "$INTERNAL_BODY" | jq -S -c .)" = "$(printf '%s' "$USER_BODY" | jq -S -c .)" ]; then
  ok 'GET /api/users/me/footprint 与内部端点返回一致（端到端链路打通）'
else
  no 'GET /api/users/me/footprint 与内部端点返回一致' "$INTERNAL_BODY" "$USER_BODY"
fi

echo
echo "== 结果：通过 $PASSED 项，失败 $FAILED 项 =="
[ "$FAILED" -eq 0 ] || exit 1
echo '委托链路验收通过：既证明了它能通，也证明了边界仍然紧闭。'
