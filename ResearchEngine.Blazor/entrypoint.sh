#!/bin/sh
set -eu

: "${API_BASE_URL:=http://localhost:8090}"
: "${BearerAuthenticationOptions__BearerTokens__0:=}"

cat > /srv/appsettings.json <<EOF
{
  "ApiBaseUrl": "${API_BASE_URL}",
  "ApiAuth": {
    "BearerToken": "${BearerAuthenticationOptions__BearerTokens__0}"
  }
}
EOF

exec caddy run --config /etc/caddy/Caddyfile --adapter caddyfile
