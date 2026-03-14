#!/bin/sh
set -eu

: "${API_BASE_URL:=http://localhost:8090}"
: "${AuthenticationOptions__ApiKeys__0:=}"

cat > /srv/appsettings.json <<EOF
{
  "ApiBaseUrl": "${API_BASE_URL}",
  "ApiAuth": {
    "ApiKey": "${AuthenticationOptions__ApiKeys__0}"
  }
}
EOF

exec caddy run --config /etc/caddy/Caddyfile --adapter caddyfile
