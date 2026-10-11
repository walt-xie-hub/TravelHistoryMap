#!/usr/bin/env bash
#
# 逐步走一遍 service-to-service 认证链，并把**每一步产出的令牌摊开给你看**（ADR-0022 / 0024 / 0025）。
#
# 与 validate-delegation.sh 的分工：
#   * validate-delegation.sh 是**闸门**：只回 [PASS]/[FAIL]，适合放进发布前检查。
#   * 本脚本是**讲解 + 核对**：打印每步的请求参数、原始响应、以及 JWT 解码后的声明，
#     让你能逐条对照"这一步应该出现什么、实际出现了什么"。断言仍然会跑，失败会标 [FAIL]。
#
# 覆盖的六个观察点（也是这条链的全部步骤）：
#   ① 身份提供方公布了什么（discovery / JWKS —— 解释了令牌头里的 kid 从哪来）
#   ② 令牌一：用户 access token（谁的用户）
#   ③ 令牌二：服务令牌 client_credentials（"谁在调"）
#   ④ 令牌三：委托令牌 token exchange（"代表谁" + "谁在调" + 目标单一 + 权限收窄）
#   ⑤ 接收侧真的验证了什么（回显 + 篡改测试：改签名 / 改 payload / alg=none）
#   ⑥ 业务端点与默认拒绝的每一道闸门
#
# 需要 bash + curl + jq。必须提供两个环境变量：
#   SUBJECT_TOKEN               当前用户的 access token
#                               （浏览器 DevTools → localStorage['travel-map.auth.token']；20 分钟过期）
#   USER_SERVICE_CLIENT_SECRET  与 docker-compose.dev.yml 同一份，在 .env 里
#
# 用法（仓库根目录；地址默认 127.0.0.1，因为 dev 端口只绑 IPv4）：
#   SUBJECT_TOKEN='<粘贴>' USER_SERVICE_CLIENT_SECRET="$(grep '^USER_SERVICE_CLIENT_SECRET=' .env | cut -d= -f2-)" \
#     bash scripts/walk-s2s-auth.sh
#
# 在容器里跑（宿主 127.0.0.1 不可达）时改三个 BASE，例如：
#   -e IDENTITY_BASE=http://host.docker.internal:8090/identity
#   -e USER_API=http://host.docker.internal:8080/api
#   -e TRAVEL_BASE=http://host.docker.internal:8081

set -euo pipefail

IDENTITY_BASE="${IDENTITY_BASE:-http://127.0.0.1:8090/identity}"
USER_API="${USER_API:-http://127.0.0.1:8080/api}"
TRAVEL_BASE="${TRAVEL_BASE:-http://127.0.0.1:8081}"
USER_CLIENT_ID="${USER_CLIENT_ID:-user-service}"
AUDIENCE="${AUDIENCE:-travel-service}"
SCOPE="${SCOPE:-travel:read}"
UNGRANTED_SCOPE="${UNGRANTED_SCOPE:-user:read}"

: "${USER_SERVICE_CLIENT_SECRET:?请提供 USER_SERVICE_CLIENT_SECRET（见文件头）}"
: "${SUBJECT_TOKEN:?请提供 SUBJECT_TOKEN（见文件头）}"

command -v curl >/dev/null || { echo '需要 curl。' >&2; exit 2; }
command -v jq >/dev/null || { echo '需要 jq。' >&2; exit 2; }

PASS=0
FAIL=0
ok() { PASS=$((PASS + 1)); printf '    [PASS] %s\n' "$1"; }
bad() { FAIL=$((FAIL + 1)); printf '    [FAIL] %s（期望 %s，实际 %s）\n' "$1" "$2" "$3"; }
expect() { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1" "$2" "$3"; fi; }
stage() { printf '\n== %s ==\n' "$1"; }
show() { printf '  %s\n' "$1"; }

status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

# ---- JWT 解包（只解码，不做验签；验签由接收侧负责，见第 5 步的篡改测试）----
b64url_dec() {
  local s="${1//-/+}"
  s="${s//_//}"
  case $((${#s} % 4)) in
    2) s="$s==" ;;
    3) s="$s=" ;;
  esac
  printf '%s' "$s" | base64 -d 2>/dev/null
}
b64url_enc() { printf '%s' "$1" | base64 | tr -d '\n' | tr '+/' '-_' | tr -d '='; }
jwt_head() { b64url_dec "$(printf '%s' "$1" | cut -d. -f1)"; }
jwt_body() { b64url_dec "$(printf '%s' "$1" | cut -d. -f2)"; }
claim() { jwt_body "$1" | jq -r "$2 // \"-\""; }

# act 是 RFC 8693 的代理链。签发侧把它作为**字符串**放进 claims 字典，
# 所以可能是 "{\"sub\":...}"（字符串）或已是对象；两种都接受。
act_sub() {
  jwt_body "$1" | jq -r 'if (.act|type)=="string" then (.act|fromjson|.sub) else (.act.sub // "-") end'
}

show_jwt() { # show_jwt <标题> <token>
  printf '  %s\n' "$1"
  printf '    header : %s\n' "$(jwt_head "$2" | jq -c . 2>/dev/null || echo '(解不开)')"
  printf '    claims : %s\n' "$(jwt_body "$2" | jq -c . 2>/dev/null || echo '(解不开)')"
}

# 换令牌（token exchange）。第 4 个参数是 subject token，默认用 SUBJECT_TOKEN；
# 第 3 个参数是 client secret，默认用真实值 —— 负例测试会覆盖它们。
exchange() { # exchange <audience> <scope> [client_secret] [subject_token]
  curl -s -X POST "$IDENTITY_BASE/token" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode 'grant_type=urn:ietf:params:oauth:grant-type:token-exchange' \
    --data-urlencode "client_id=$USER_CLIENT_ID" \
    --data-urlencode "client_secret=${3:-$USER_SERVICE_CLIENT_SECRET}" \
    --data-urlencode "subject_token=${4:-$SUBJECT_TOKEN}" \
    --data-urlencode 'subject_token_type=urn:ietf:params:oauth:token-type:access_token' \
    --data-urlencode "audience=$1" \
    --data-urlencode "scope=$2"
}

stage '0. 环境是否就绪（三个服务都要在跑）'
show "identity : $IDENTITY_BASE"
show "user-api : $USER_API"
show "travel   : $TRAVEL_BASE"
expect 'identity 的 OIDC 发现可达' 200 "$(status "$IDENTITY_BASE/.well-known/openid-configuration")"
expect 'travel-service 健康' 200 "$(status "$TRAVEL_BASE/health")"
expect 'user-service 健康' 200 "$(status "${USER_API%/api}/health")"

stage '1. 身份提供方公布的事实（先建立"谁有权"的地图）'
DISCOVERY="$(curl -s "$IDENTITY_BASE/.well-known/openid-configuration")"
printf '%s\n' "$DISCOVERY" | jq '{issuer, jwks_uri, token_endpoint, grant_types_supported, scopes_supported}'
JWKS="$(curl -s "$IDENTITY_BASE/jwks")"
show 'JWKS 里的签名密钥（令牌头部的 kid 必须出现在这里，否则接收方验签必失败）：'
printf '%s\n' "$JWKS" | jq -r '.keys[] | "      kid=\(.kid)  alg=\(.alg)  kty=\(.kty)"'
KIDS="$(printf '%s' "$JWKS" | jq -r '[.keys[].kid] | join(",")')"
show '→ 允许被调的服务清单（AllowedServiceAudiences）**不对外公布**：只能靠行为验证（见第 7 步的 invalid_target）。'

stage '2. 令牌①：用户 access token —— 回答"这是谁的用户"'
show_jwt '解码（只解码，不验签）：' "$SUBJECT_TOKEN"
SUBJECT_SUB="$(claim "$SUBJECT_TOKEN" '.sub')"
SUBJECT_KID="$(jwt_head "$SUBJECT_TOKEN" | jq -r '.kid // "-"')"
printf '  → 有效期约 %s 分钟（exp - iat = %s 秒）\n' "$((($(claim "$SUBJECT_TOKEN" '.exp') - $(claim "$SUBJECT_TOKEN" '.iat')) / 60))" "$(($(claim "$SUBJECT_TOKEN" '.exp') - $(claim "$SUBJECT_TOKEN" '.iat')))"
expect 'aud = 客户端标识（不是某个服务）' 'travel-map-client' "$(claim "$SUBJECT_TOKEN" '.aud')"
expect '没有 act 声明（act 是委托令牌专有的）' '-' "$(claim "$SUBJECT_TOKEN" '.act')"
case ",$KIDS," in
  *",$SUBJECT_KID,"*) ok "kid=$SUBJECT_KID 能在 JWKS 里找到" ;;
  *) bad 'kid 能在 JWKS 里找到' "$KIDS" "$SUBJECT_KID" ;;
esac

stage '3. 令牌②：服务令牌（client_credentials）—— 回答"谁在调"'
show "请求：POST $IDENTITY_BASE/token"
printf '      grant_type=client_credentials  client_id=%s  client_secret=<%s 字符>\n' \
  "$USER_CLIENT_ID" "${#USER_SERVICE_CLIENT_SECRET}"
printf '      audience=%s  scope=（空；client_credentials 允许空 scope，委托**不允许**）\n' "$AUDIENCE"
SERVICE_RESPONSE="$(curl -s -X POST "$IDENTITY_BASE/token" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode 'grant_type=client_credentials' \
  --data-urlencode "client_id=$USER_CLIENT_ID" \
  --data-urlencode "client_secret=$USER_SERVICE_CLIENT_SECRET" \
  --data-urlencode "audience=$AUDIENCE" \
  --data-urlencode 'scope=')"
printf '  响应：%s\n' "$(printf '%s' "$SERVICE_RESPONSE" | jq -c '{token_type, expires_in, scope, has_access_token: (.access_token != null)}')"
SERVICE_TOKEN="$(printf '%s' "$SERVICE_RESPONSE" | jq -r '.access_token // empty')"
show_jwt '解码：' "$SERVICE_TOKEN"
expect 'sub = service:user-service（"谁在调"）' "service:$USER_CLIENT_ID" "$(claim "$SERVICE_TOKEN" '.sub')"
expect 'aud = 被调服务' "$AUDIENCE" "$(claim "$SERVICE_TOKEN" '.aud')"
expect '**没有** act —— 这就是它冒充不了用户委托的判据' '-' "$(claim "$SERVICE_TOKEN" '.act')"
expect 'TTL = 300 秒（exp - iat）' 300 "$(( $(claim "$SERVICE_TOKEN" '.exp') - $(claim "$SERVICE_TOKEN" '.iat') ))"

stage '4. 令牌③：用户委托令牌（token exchange，RFC 8693）—— "代表谁" + "谁在调"'
show "请求：POST $IDENTITY_BASE/token"
show '      grant_type=urn:ietf:params:oauth:grant-type:token-exchange'
printf '      client_id=%s  client_secret=<调用方自己的服务凭证，即 actor>\n' "$USER_CLIENT_ID"
printf '      subject_token=<第 2 步那枚用户令牌>  subject_token_type=urn:ietf:params:oauth:token-type:access_token\n'
printf '      audience=%s  scope=%s（scope 必填且必须已授予）\n' "$AUDIENCE" "$SCOPE"
DELEGATED_RESPONSE="$(exchange "$AUDIENCE" "$SCOPE")"
printf '  响应：%s\n' "$(printf '%s' "$DELEGATED_RESPONSE" | jq -c '{token_type, issued_token_type, expires_in, scope}')"
DELEGATED="$(printf '%s' "$DELEGATED_RESPONSE" | jq -r '.access_token // empty')"
if [ -z "$DELEGATED" ]; then
  printf '  [FAIL] 没换到委托令牌：%s\n' "$(printf '%s' "$DELEGATED_RESPONSE" | jq -c .)"
  printf '\n== 结果：通过 %s 项，失败 %s 项 ==\n' "$PASS" "$((FAIL + 1))"
  exit 1
fi
show_jwt '解码：' "$DELEGATED"
DEL_SUB="$(claim "$DELEGATED" '.sub')"
DEL_ACT="$(act_sub "$DELEGATED")"
DEL_TTL="$(( $(claim "$DELEGATED" '.exp') - $(claim "$DELEGATED" '.iat') ))"
expect 'sub = 用户 id（与第 2 步那枚用户令牌一致）' "$SUBJECT_SUB" "$DEL_SUB"
expect 'act.sub = service:user-service（RFC 8693 代理链）' "service:$USER_CLIENT_ID" "$DEL_ACT"
expect 'aud = 被调服务（一个令牌只对一个目标有效）' "$AUDIENCE" "$(claim "$DELEGATED" '.aud')"
expect 'scope = 本次调用被收窄到的权限' "$SCOPE" "$(claim "$DELEGATED" '.scope')"
expect 'TTL = 300 秒（刻意与**服务**令牌同寿，而不是沿用用户令牌的 20 分钟）' 300 "$DEL_TTL"
printf '  → 三枚令牌对照（看 sub / act / aud / TTL 四处差别）：\n'
printf '      用户令牌   sub=%-22s act=无   aud=%-18s TTL=%s\n' "$SUBJECT_SUB" 'travel-map-client' "$(( $(claim "$SUBJECT_TOKEN" '.exp') - $(claim "$SUBJECT_TOKEN" '.iat') ))"
printf '      服务令牌   sub=%-22s act=无   aud=%-18s TTL=%s\n' "service:$USER_CLIENT_ID" "$AUDIENCE" "$(( $(claim "$SERVICE_TOKEN" '.exp') - $(claim "$SERVICE_TOKEN" '.iat') ))"
printf '      委托令牌   sub=%-22s act=有   aud=%-18s TTL=%s\n' "$DEL_SUB" "$AUDIENCE" "$DEL_TTL"

stage '5. 接收侧（travel-service）到底验证了什么'
show "a) 回显：GET $TRAVEL_BASE/internal/whoami"
WHOAMI="$(curl -s -H "Authorization: Bearer $DELEGATED" "$TRAVEL_BASE/internal/whoami")"
printf '     %s\n' "$(printf '%s' "$WHOAMI" | jq -c .)"
expect '回显的 subject = 令牌里的 sub' "$DEL_SUB" "$(printf '%s' "$WHOAMI" | jq -r '.subject // "-"')"
expect '回显的 actingService = 令牌里的 act.sub' "service:$USER_CLIENT_ID" "$(printf '%s' "$WHOAMI" | jq -r '.actingService // "-"')"
expect '回显的 scopes = 令牌里的 scope' "$SCOPE" "$(printf '%s' "$WHOAMI" | jq -r '.scopes | join(" ")')"

show 'b) 篡改测试 —— 证明接收侧真的**验签**，而不是"解出 JSON 就信"：'
H="$(printf '%s' "$DELEGATED" | cut -d. -f1)"
P="$(printf '%s' "$DELEGATED" | cut -d. -f2)"
S="$(printf '%s' "$DELEGATED" | cut -d. -f3)"
expect '改签名 -> 401' 401 "$(status -H "Authorization: Bearer $H.$P.${S%?}X" "$TRAVEL_BASE/internal/whoami")"
TAMPERED_PAYLOAD="$(b64url_enc "$(jwt_body "$DELEGATED" | jq -c '.sub = "1"')")"
expect '改 payload（sub→1）但沿用原签名 -> 401' 401 "$(status -H "Authorization: Bearer $H.$TAMPERED_PAYLOAD.$S" "$TRAVEL_BASE/internal/whoami")"
NONE_HEADER="$(b64url_enc '{"alg":"none","typ":"JWT"}')"
expect 'alg=none（算法混淆）-> 401' 401 "$(status -H "Authorization: Bearer $NONE_HEADER.$P." "$TRAVEL_BASE/internal/whoami")"
OTHER_AUDIENCE_TOKEN="$(exchange 'identity-service' "$SCOPE" | jq -r '.access_token // empty')"
if [ -n "$OTHER_AUDIENCE_TOKEN" ]; then
  expect '换一枚 aud=identity-service 的委托令牌，拿到 travel 上 -> 401（aud 精确匹配）' 401 \
    "$(status -H "Authorization: Bearer $OTHER_AUDIENCE_TOKEN" "$TRAVEL_BASE/internal/whoami")"
else
  bad '换一枚 aud=identity-service 的委托令牌' 'token' '(空)'
fi

stage '6. 业务端点（ADR-0025）：用委托令牌读 travel 的聚合'
show "a) 内部端点：GET $TRAVEL_BASE/internal/travels/footprint"
FOOTPRINT="$(curl -s -H "Authorization: Bearer $DELEGATED" "$TRAVEL_BASE/internal/travels/footprint")"
printf '     %s\n' "$(printf '%s' "$FOOTPRINT" | jq -c .)"
expect '委托令牌可用（200 且是 FootprintOverview）' true "$(printf '%s' "$FOOTPRINT" | jq -r 'has("totalRecords")')"
show "b) 端到端：GET $USER_API/users/me/footprint（浏览器只调 user-service，跨服务那一跳由它自己完成）"
FOOTPRINT_E2E="$(curl -s -H "Authorization: Bearer $SUBJECT_TOKEN" "$USER_API/users/me/footprint")"
printf '     %s\n' "$(printf '%s' "$FOOTPRINT_E2E" | jq -c .)"
expect '与内部端点返回逐字段一致' "$(printf '%s' "$FOOTPRINT" | jq -S -c .)" "$(printf '%s' "$FOOTPRINT_E2E" | jq -S -c .)"

stage '7. 负例：每一道闸门都必须是"默认拒绝"'
expect '错误 client_secret -> invalid_client' invalid_client "$(exchange "$AUDIENCE" "$SCOPE" 'definitely-not-the-secret' | jq -r '.error // empty')"
expect 'audience 未登记 -> invalid_target' invalid_target "$(exchange 'not-a-registered-service' "$SCOPE" | jq -r '.error // empty')"
expect '缺 scope -> invalid_scope（否则委托令牌退化成用户令牌的副本）' invalid_scope "$(exchange "$AUDIENCE" '' | jq -r '.error // empty')"
expect "未授予的 scope（$UNGRANTED_SCOPE）-> invalid_scope" invalid_scope "$(exchange "$AUDIENCE" "$UNGRANTED_SCOPE" | jq -r '.error // empty')"
expect '伪造的 subject_token -> invalid_grant' invalid_grant "$(exchange "$AUDIENCE" "$SCOPE" '' 'aaa.bbb.ccc' | jq -r '.error // empty')"
expect '无令牌调内部端点 -> 401' 401 "$(status "$TRAVEL_BASE/internal/whoami")"
expect '用户令牌调内部端点 -> 401（它的 aud 是客户端标识）' 401 "$(status -H "Authorization: Bearer $SUBJECT_TOKEN" "$TRAVEL_BASE/internal/whoami")"
expect '服务令牌调内部端点 -> 403（aud 对但缺 act）' 403 "$(status -H "Authorization: Bearer $SERVICE_TOKEN" "$TRAVEL_BASE/internal/whoami")"

printf '\n== 结果：通过 %s 项，失败 %s 项 ==\n' "$PASS" "$FAIL"
printf '  令牌是怎么流转的：\n'
printf '    用户令牌  -> 只到 identity 为止（它**不会**流向 travel-service）\n'
printf '    服务令牌  -> 只用于证明调用方身份；对 /internal/* 业务端点会被 403 挡住\n'
printf '    委托令牌  -> 服务之间流动的那一枚：sub=用户、act=调用方、aud=目标、scope=收窄后的权限\n'
if [ "$FAIL" -gt 0 ]; then
  exit 1
fi
