#!/bin/sh
set -eu

: "${API_BASE_URL:=http://localhost:8090}"
: "${APP_VERSION:=dev}"
: "${AuthenticationOptions__ApiKeys__0:=}"

cat > /srv/appsettings.json <<EOF
{
  "ApiBaseUrl": "${API_BASE_URL}",
  "AppVersion": "${APP_VERSION}",
  "ApiAuth": {
    "ApiKey": "${AuthenticationOptions__ApiKeys__0}"
  }
}
EOF

exec caddy run --config /etc/caddy/Caddyfile --adapter caddyfile
