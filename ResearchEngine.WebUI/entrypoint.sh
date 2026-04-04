#!/bin/sh
set -eu

: "${APP_VERSION:=dev}"
: "${AuthenticationOptions__ApiKeys__0:=}"

if [ -z "${API_BASE_URL:-}" ]; then
  API_BASE_URL="$(sed -n 's/^[[:space:]]*"ApiBaseUrl":[[:space:]]*"\(.*\)",[[:space:]]*$/\1/p' /srv/appsettings.json | head -n 1)"
fi

: "${API_BASE_URL:?API_BASE_URL is required or /srv/appsettings.json must define ApiBaseUrl}"

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
