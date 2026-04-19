#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_DIR"

docker compose up -d --build
docker compose ps

echo
echo "Main API:  http://SERVER_IP:98/api/system/status"
echo "Swagger:   http://SERVER_IP:98/swagger"
echo "Dashboard: http://SERVER_IP:98/dashboard/"
